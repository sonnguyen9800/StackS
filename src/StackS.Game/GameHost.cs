using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using StackS.Kernel;

namespace StackS.Game;

public sealed class GameOptions
{
    public LevelDoc Level = Levels.Default();
    public bool Ps1 = true;
    public int LowResHeight = 240;
    public string? RecordPath;          // write a replay file on exit
    public ReplayFile? Playback;        // drive the game from a recorded run instead of input
    public int QuitAfterTicks;          // > 0: quit after this many simulation ticks (smoke tests)
    public Action<string>? Log;
}

/// <summary>
/// The native game: owns the simulation, maps input to the same input bits the editor records,
/// and draws the level through the platform layer. Identical on desktop and mobile.
/// </summary>
public static unsafe class GameHost
{
    static GameOptions opt = new();
    static SimState sim = null!;
    static readonly List<int> recorded = new();
    static readonly HashSet<int> keys = new();
    static double acc;
    static int frames, playbackIndex, tuneIndex, lastDeaths;
    static bool goalLogged;
    static float time, facing;
    static Vector3 camPos; static bool camInit;
    // touch controls: left half = virtual stick, right half = jump
    static ulong stickId, jumpId; static bool stickOn, jumpOn; static Vector2 stickStart, stickNow;

    public static void Configure(GameOptions o)
    {
        opt = o;
        sim = Sim.Create(o.Level);
        recorded.Clear(); keys.Clear(); acc = 0; frames = 0; playbackIndex = 0; tuneIndex = 0; lastDeaths = 0; goalLogged = false; camInit = false;
    }

    /// <summary>Fills the callback table for the platform layer (desktop: sp_run, mobile: stacks_configure).</summary>
    public static void FillCallbacks(Native.Callbacks* cb, byte* title)
    {
        cb->Init = &OnInit;
        cb->Frame = &OnFrame;
        cb->OnEvent = &OnEvent;
        cb->Cleanup = &OnCleanup;
        cb->Width = 1280; cb->Height = 720;
        cb->Title = title;
    }

    public static void RunDesktop(GameOptions o)
    {
        Configure(o);
        var title = "StackS"u8;
        fixed (byte* t = title)
        {
            // u8 literals are null-terminated in memory
            Native.Callbacks cb;
            FillCallbacks(&cb, t);
            Native.Run(&cb);
        }
    }

    [UnmanagedCallersOnly] static void OnInit() => opt.Log?.Invoke("StackS native host started: " + opt.Level.Parts.Count + " parts");

    [UnmanagedCallersOnly] static void OnCleanup()
    {
        if (opt.RecordPath != null)
        {
            var r = new ReplayFile { Name = Path.GetFileNameWithoutExtension(opt.RecordPath), Level = opt.Level, Inputs = recorded.ToList(), ExpectedKey = Sim.StateKey(sim) };
            File.WriteAllText(opt.RecordPath, LevelSerializer.SaveReplay(r));
            opt.Log?.Invoke($"recorded {recorded.Count} ticks to {opt.RecordPath}");
        }
        opt.Log?.Invoke("end state " + Sim.Fnv(Sim.StateKey(sim)) + $" (tick {sim.Tick}, coins {sim.Coins.Count}, deaths {sim.Deaths})");
    }

    [UnmanagedCallersOnly] static void OnEvent(Native.Event* e)
    {
        switch (e->Type)
        {
            case Native.KeyDown:
                keys.Add(e->Key);
                if (e->Key == Native.KeyEscape) Native.Quit();
                if (e->Key == Native.KeyP) opt.Ps1 = !opt.Ps1;
                if (e->Key == Native.KeyR) Configure(opt);
                break;
            case Native.KeyUp: keys.Remove(e->Key); break;
            case Native.FocusLost: keys.Clear(); stickOn = jumpOn = false; break;
            case Native.TouchBegin:
            case Native.TouchMove:
            case Native.TouchEnd:
            case Native.TouchCancel:
                for (int i = 0; i < e->NumTouches; i++)
                {
                    var t = e->Get(i);
                    if (t.Changed == 0) continue;
                    bool down = e->Type == Native.TouchBegin, up = e->Type is Native.TouchEnd or Native.TouchCancel;
                    var p = new Vector2(t.X, t.Y);
                    if (down)
                    {
                        if (p.X < e->Width * 0.5f && !stickOn) { stickOn = true; stickId = t.Id; stickStart = stickNow = p; }
                        else if (!jumpOn) { jumpOn = true; jumpId = t.Id; }
                    }
                    else if (up)
                    {
                        if (stickOn && t.Id == stickId) stickOn = false;
                        if (jumpOn && t.Id == jumpId) jumpOn = false;
                    }
                    else if (stickOn && t.Id == stickId) stickNow = p;
                }
                break;
        }
    }

    static int InputBits()
    {
        int b = 0;
        if (keys.Contains(Native.KeyLeft) || keys.Contains(Native.KeyA)) b |= Sim.Left;
        if (keys.Contains(Native.KeyRight) || keys.Contains(Native.KeyD)) b |= Sim.Right;
        if (keys.Contains(Native.KeyUp_) || keys.Contains(Native.KeyW)) b |= Sim.Forward;
        if (keys.Contains(Native.KeyDown_) || keys.Contains(Native.KeyS)) b |= Sim.Back;
        if (keys.Contains(Native.KeySpace) || keys.Contains(Native.KeyK)) b |= Sim.Jump;
        if (stickOn)
        {
            var d = stickNow - stickStart; float dead = 24;
            if (d.X < -dead) b |= Sim.Left; if (d.X > dead) b |= Sim.Right;
            if (d.Y < -dead) b |= Sim.Forward; if (d.Y > dead) b |= Sim.Back;
        }
        if (jumpOn) b |= Sim.Jump;
        return b;
    }

    [UnmanagedCallersOnly] static void OnFrame()
    {
        double dt = Math.Min(0.1, Native.FrameDuration());
        time += (float)dt;
        acc += dt;
        int steps = 0;
        while (acc >= Sim.DT && steps < 5)
        {
            int input;
            if (opt.Playback != null)
            {
                var pb = opt.Playback;
                while (tuneIndex < pb.Tune.Count && pb.Tune[tuneIndex].Tick <= playbackIndex)
                {
                    sim.Tuning.Set(pb.Tune[tuneIndex].Key, pb.Tune[tuneIndex].Value);
                    tuneIndex++;
                }
                input = playbackIndex < pb.Inputs.Count ? pb.Inputs[playbackIndex++] : 0;
            }
            else input = InputBits();
            recorded.Add(input);
            Sim.Step(sim, input);
            acc -= Sim.DT; steps++;
        }
        if (steps == 5) acc = 0;
        if (sim.Deaths != lastDeaths) { lastDeaths = sim.Deaths; opt.Log?.Invoke("respawned (deaths: " + sim.Deaths + ")"); }
        if (sim.Done && !goalLogged) { goalLogged = true; opt.Log?.Invoke($"goal reached in {sim.Tick / 60.0:F1} s with {sim.Coins.Count} coins"); }
        if (opt.Playback != null && playbackIndex == opt.Playback.Inputs.Count && playbackIndex > 0 && steps > 0)
        {
            bool match = Sim.StateKey(sim) == opt.Playback.ExpectedKey;
            opt.Log?.Invoke("playback finished: " + (match ? "matches the recorded end state" : "DOES NOT match the recorded end state"));
            playbackIndex++;   // log once
        }
        Draw();
        frames++;
        if (opt.QuitAfterTicks > 0 && sim.Tick >= opt.QuitAfterTicks) Native.Quit();
    }

    static void Draw()
    {
        int w = Math.Max(1, Native.Width()), h = Math.Max(1, Native.Height());
        var target = new Vector3((float)sim.X, (float)sim.Y + 0.4f, (float)sim.Z);
        var want = new Vector3((float)sim.X, (float)sim.Y + 3.2f, (float)sim.Z + 6.5f);
        camPos = camInit ? Vector3.Lerp(camPos, want, 0.12f) : want; camInit = true;
        var view = Matrix4x4.CreateLookAt(camPos, target, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, (float)w / h, 0.1f, 200f);
        var vp = view * proj;

        bool ps1 = opt.Ps1;
        var fog = ps1 ? new Vector3(0x2a / 255f, 0x23 / 255f, 0x40 / 255f) : new Vector3(0xbf / 255f, 0xe0 / 255f, 0xf5 / 255f);
        Native.Begin((float*)&fog, ps1 ? 12 : 40, ps1 ? 40 : 120, ps1 ? 1 : 0, opt.LowResHeight);

        foreach (var p in sim.Parts)
        {
            float yaw = 0;
            if (p.Kind == PartKind.Coin)
            {
                if (sim.Coins.Contains(p.Id)) continue;
                yaw = time * 2;
            }
            var t = PartTypes.All[(int)p.Kind];
            // Large parts are drawn as tiles of at most 2 m so affine texture warping stays subtle (as the PS1 did).
            int nx = Math.Max(1, (int)Math.Ceiling(p.W / 2)), nz = Math.Max(1, (int)Math.Ceiling(p.D / 2));
            float tw = (float)p.W / nx, td = (float)p.D / nz;
            for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
            {
                float cx = (float)p.X - (float)p.W / 2 + tw * (ix + 0.5f), cz = (float)p.Z - (float)p.D / 2 + td * (iz + 0.5f);
                var model = Matrix4x4.CreateScale(tw, (float)p.H, td) * Matrix4x4.CreateRotationY(yaw)
                            * Matrix4x4.CreateTranslation(cx, (float)p.Y, cz);
                DrawBox(model, view, vp, t.Color, yaw, 1);
            }
        }

        // segmented hero: rigid boxes, no skinning
        double speed = Math.Sqrt(sim.Vx * sim.Vx + sim.Vz * sim.Vz);
        if (speed > 0.3) facing = MathF.Atan2((float)sim.Vx, (float)sim.Vz);
        float swing = speed > 0.3 && sim.Grounded ? MathF.Sin(time * 14) * 0.6f : 0;
        var root = Matrix4x4.CreateRotationY(facing) * Matrix4x4.CreateTranslation((float)sim.X, (float)(sim.Y - Sim.HY), (float)sim.Z);
        Part(new(0.46f, 0.42f, 0.28f), new(0, 0.56f, 0), 0, 0xd9483b);   // body
        Part(new(0.30f, 0.30f, 0.30f), new(0, 0.92f, 0), 0, 0xf1c9a0);   // head
        Part(new(0.13f, 0.38f, 0.13f), new(-0.3f, 0.56f, 0), 0, 0xd9483b);
        Part(new(0.13f, 0.38f, 0.13f), new(0.3f, 0.56f, 0), 0, 0xd9483b);
        Leg(-0.11f, swing); Leg(0.11f, -swing);
        Native.End();

        void Part(Vector3 size, Vector3 local, float rotX, uint color)
        {
            var m = Matrix4x4.CreateScale(size) * Matrix4x4.CreateRotationX(rotX) * Matrix4x4.CreateTranslation(local) * root;
            DrawBox(m, view, vp, color, facing, 1);
        }
        void Leg(float x, float rot)
        {
            var m = Matrix4x4.CreateScale(0.17f, 0.36f, 0.17f) * Matrix4x4.CreateTranslation(0, -0.18f, 0)
                    * Matrix4x4.CreateRotationX(rot) * Matrix4x4.CreateTranslation(x, 0.36f, 0) * root;
            DrawBox(m, view, vp, 0x2f4f9a, facing, 1);
        }
    }

    static void DrawBox(Matrix4x4 model, Matrix4x4 view, Matrix4x4 vp, uint rgb, float yaw, float rep)
    {
        var mvp = model * vp;
        var mv = model * view;
        Native.Box((float*)&mvp, (float*)&mv, ((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f,
                   MathF.Cos(yaw), MathF.Sin(yaw), rep);
    }
}
