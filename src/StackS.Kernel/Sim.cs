using System.Text;

namespace StackS.Kernel;

public struct Vec3
{
    public double X, Y, Z;
    public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
}

public enum PartKind : byte { Solid, Spawn, Checkpoint, Coin, Goal, Kill }

/// <summary>Read-only copy of a part as the simulation sees it.</summary>
public readonly struct SimPart
{
    public readonly string Id;
    public readonly PartKind Kind;
    public readonly bool Solid;
    public readonly double X, Y, Z, W, H, D;

    public SimPart(Part p)
    {
        Id = p.Id;
        Kind = p.Type switch
        {
            "solid" => PartKind.Solid, "spawn" => PartKind.Spawn, "checkpoint" => PartKind.Checkpoint,
            "coin" => PartKind.Coin, "goal" => PartKind.Goal, _ => PartKind.Kill,
        };
        Solid = Kind == PartKind.Solid;
        X = p.X; Y = p.Y; Z = p.Z; W = p.W; H = p.H; D = p.D;
    }
}

public sealed class SimState
{
    public double X, Y, Z, Vx, Vy, Vz, Coyote;
    public bool Grounded, Jumping, Cut, PrevJ, Done;
    public int Buf, Deaths, Tick;
    public Vec3 Respawn;
    public readonly List<string> Coins = new();
    public Tuning Tuning = new();
    public SimPart[] Parts = Array.Empty<SimPart>();
}

/// <summary>
/// The deterministic platformer simulation. Fixed 60 Hz tick, inputs as bits.
/// RULES: only + - * / and Math.Abs; no Math.Sin/Cos/Exp/Pow, no Random, no clock, no allocation in Step
/// (except when a coin list grows). It must stay operation-for-operation identical to editor/sim.js,
/// which is checked bit-for-bit by the golden replays.
/// </summary>
public static class Sim
{
    public const double DT = 1.0 / 60, HX = 0.3, HY = 0.45, HZ = 0.3, EPS = 1e-4, DIAG = 0.7071067811865476;
    public const int Left = 1, Right = 2, Forward = 4, Back = 8, Jump = 16;

    public static SimState Create(LevelDoc doc)
    {
        Vec3 r = new(0, 2, 0);
        foreach (var p in doc.Parts)
            if (p.Type == "spawn") { r = new Vec3(p.X, p.Y + p.H / 2 + HY + 0.01, p.Z); break; }
        var s = new SimState { X = r.X, Y = r.Y, Z = r.Z, Respawn = r, Tuning = doc.Tuning.Clone() };
        s.Parts = new SimPart[doc.Parts.Count];
        for (int i = 0; i < s.Parts.Length; i++) s.Parts[i] = new SimPart(doc.Parts[i]);
        return s;
    }

    static bool Overlap(SimState s, in SimPart p) =>
        Math.Abs(s.X - p.X) < HX + p.W / 2 && Math.Abs(s.Y - p.Y) < HY + p.H / 2 && Math.Abs(s.Z - p.Z) < HZ + p.D / 2;

    static int MoveAxis(SimState s, int ax, double d)
    {
        if (d == 0) return 0;
        if (ax == 0) s.X += d; else if (ax == 1) s.Y += d; else s.Z += d;
        double half = ax == 0 ? HX : ax == 1 ? HY : HZ;
        int hit = 0;
        var parts = s.Parts;
        for (int i = 0; i < parts.Length; i++)
        {
            ref readonly var p = ref parts[i];
            if (!p.Solid || !Overlap(s, p)) continue;
            double c = ax == 0 ? p.X : ax == 1 ? p.Y : p.Z;
            double size = ax == 0 ? p.W : ax == 1 ? p.H : p.D;
            double v;
            if (d > 0) { v = c - size / 2 - half - EPS; hit = 1; }
            else { v = c + size / 2 + half + EPS; hit = -1; }
            if (ax == 0) s.X = v; else if (ax == 1) s.Y = v; else s.Z = v;
        }
        return hit;
    }

    static void Die(SimState s)
    {
        s.Deaths++;
        s.X = s.Respawn.X; s.Y = s.Respawn.Y; s.Z = s.Respawn.Z;
        s.Vx = s.Vy = s.Vz = 0;
        s.Jumping = false;
    }

    public static void Step(SimState s, int inp)
    {
        if (s.Done) { s.Tick++; return; }
        var t = s.Tuning;
        double g = 2 * t.JumpHeight / (t.TimeToApex * t.TimeToApex), v0 = 2 * t.JumpHeight / t.TimeToApex;
        double dx = ((inp & Right) != 0 ? 1 : 0) - ((inp & Left) != 0 ? 1 : 0);
        double dz = ((inp & Back) != 0 ? 1 : 0) - ((inp & Forward) != 0 ? 1 : 0);
        if (dx != 0 && dz != 0) { dx *= DIAG; dz *= DIAG; }
        s.Vx += (dx * t.MoveSpeed - s.Vx) * 0.25;
        s.Vz += (dz * t.MoveSpeed - s.Vz) * 0.25;
        bool j = (inp & Jump) != 0;
        if (j && !s.PrevJ) s.Buf = 6; else if (s.Buf > 0) s.Buf--;
        if (s.Grounded) s.Coyote = t.CoyoteTicks + 1; else if (s.Coyote > 0) s.Coyote--;
        if (s.Buf > 0 && s.Coyote > 0) { s.Vy = v0; s.Buf = 0; s.Coyote = 0; s.Grounded = false; s.Jumping = true; s.Cut = false; }
        if (!j && s.Jumping && !s.Cut && s.Vy > 0) { s.Vy *= 0.5; s.Cut = true; }
        s.Vy -= g * DT; if (s.Vy < -30) s.Vy = -30;
        if (MoveAxis(s, 0, s.Vx * DT) != 0) s.Vx = 0;
        if (MoveAxis(s, 2, s.Vz * DT) != 0) s.Vz = 0;
        int hy = MoveAxis(s, 1, s.Vy * DT);
        if (hy != 0) s.Vy = 0;
        s.Grounded = hy == -1;
        if (s.Grounded) s.Jumping = false;
        var parts = s.Parts;
        for (int i = 0; i < parts.Length; i++)
        {
            ref readonly var p = ref parts[i];
            if (p.Solid || !Overlap(s, p)) continue;
            if (p.Kind == PartKind.Coin) { if (!s.Coins.Contains(p.Id)) s.Coins.Add(p.Id); }
            else if (p.Kind == PartKind.Checkpoint) s.Respawn = new Vec3(p.X, p.Y + p.H / 2 + HY + 0.01, p.Z);
            else if (p.Kind == PartKind.Goal) s.Done = true;
            else if (p.Kind == PartKind.Kill) { Die(s); break; }
        }
        if (s.Y < -15) Die(s);
        s.PrevJ = j;
        s.Tick++;
    }

    /// <summary>Canonical end-state key: exact IEEE bits of position and velocity, so two runs match only if they are bit-identical.</summary>
    public static string StateKey(SimState s)
    {
        var sb = new StringBuilder(160);
        void B(double v) => sb.Append(((ulong)BitConverter.DoubleToInt64Bits(v)).ToString("x16"));
        B(s.X); sb.Append(','); B(s.Y); sb.Append(','); B(s.Z); sb.Append(',');
        B(s.Vx); sb.Append(','); B(s.Vy); sb.Append(','); B(s.Vz);
        sb.Append('|').Append(string.Join(",", s.Coins));
        sb.Append('|').Append(s.Deaths).Append('|').Append(s.Tick).Append('|').Append(s.Done ? 1 : 0);
        return sb.ToString();
    }

    public static string Fnv(string str)
    {
        uint h = 0x811c9dc5;
        foreach (char c in str) { h ^= c; h *= 0x01000193; }
        return h.ToString("x8");
    }

    /// <summary>Re-simulate a recorded run headless.</summary>
    public static SimState Replay(LevelDoc start, IReadOnlyList<int> inputs, IReadOnlyList<TuneEvent> tune)
    {
        var s = Create(start);
        int k = 0;
        for (int i = 0; i < inputs.Count; i++)
        {
            while (k < tune.Count && tune[k].Tick <= i) { s.Tuning.Set(tune[k].Key, tune[k].Value); k++; }
            Step(s, inputs[i]);
        }
        return s;
    }
}
