using System.Runtime.InteropServices;

namespace StackS.Game;

/// <summary>P/Invoke surface of native/stacks_platform.h. Layouts must match the C structs exactly.</summary>
public static unsafe partial class Native
{
    public const string Lib = "stacks_platform";

    public const int KeyDown = 1, KeyUp = 2, TouchBegin = 3, TouchMove = 4, TouchEnd = 5, TouchCancel = 6, Resized = 7, FocusLost = 8;
    public const int KeySpace = 32, KeyA = 65, KeyD = 68, KeyK = 75, KeyP = 80, KeyR = 82, KeyS = 83, KeyW = 87;
    public const int KeyEscape = 256, KeyTab = 258, KeyRight = 262, KeyLeft = 263, KeyDown_ = 264, KeyUp_ = 265;

    [StructLayout(LayoutKind.Sequential)]
    public struct Touch { public ulong Id; public float X, Y; public int Changed; public int Pad; }

    [StructLayout(LayoutKind.Sequential)]
    public struct Event
    {
        public int Type, Key, NumTouches, Width, Height, Pad;
        public Touch T0, T1, T2, T3;
        public Touch Get(int i) => i switch { 0 => T0, 1 => T1, 2 => T2, _ => T3 };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Callbacks
    {
        public delegate* unmanaged<void> Init;
        public delegate* unmanaged<void> Frame;
        public delegate* unmanaged<Event*, void> OnEvent;
        public delegate* unmanaged<void> Cleanup;
        public int Width, Height;
        public byte* Title;
    }

    [LibraryImport(Lib, EntryPoint = "sp_run")] public static partial void Run(Callbacks* cb);
    [LibraryImport(Lib, EntryPoint = "sp_frame_duration")] public static partial double FrameDuration();
    [LibraryImport(Lib, EntryPoint = "sp_width")] public static partial int Width();
    [LibraryImport(Lib, EntryPoint = "sp_height")] public static partial int Height();
    [LibraryImport(Lib, EntryPoint = "sp_quit")] public static partial void Quit();
    [LibraryImport(Lib, EntryPoint = "sp_begin")] public static partial void Begin(float* fogRgb, float fogNear, float fogFar, int ps1, int lowResHeight);
    [LibraryImport(Lib, EntryPoint = "sp_box")] public static partial void Box(float* mvp, float* mv, float r, float g, float b, float yawCos, float yawSin, float texRepeat);
    [LibraryImport(Lib, EntryPoint = "sp_end")] public static partial void End();
}
