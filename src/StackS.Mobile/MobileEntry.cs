using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using StackS.Game;
using StackS.Kernel;

namespace StackS.Mobile;

/// <summary>
/// Entry point for Android and iOS. The platform layer's sokol_main() calls stacks_configure()
/// once at startup; from then on sokol_app drives the same GameHost callbacks as on desktop.
/// </summary>
public static unsafe class MobileEntry
{
    [UnmanagedCallersOnly(EntryPoint = "stacks_configure")]
    public static void Configure(Native.Callbacks* cb)
    {
        var o = new GameOptions { Level = LoadLevel(), Log = Log };
        GameHost.Configure(o);
        var title = "StackS"u8;   // u8 literals live in static data and are null-terminated
        GameHost.FillCallbacks(cb, (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(title)));
    }

    static LevelDoc LoadLevel()
    {
        var asm = typeof(MobileEntry).Assembly;
        foreach (var name in new[] { "current.level.json", "starter.level.json" })
        {
            using var s = asm.GetManifestResourceStream(name);
            if (s == null) continue;
            using var r = new StreamReader(s);
            Log("level: " + name);
            return LevelSerializer.Load(r.ReadToEnd());
        }
        return Levels.Default();
    }

    static void Log(string msg)
    {
        if (OperatingSystem.IsAndroid()) AndroidLog.Write(msg);
        else Console.WriteLine(msg);
    }
}

static partial class AndroidLog
{
    [LibraryImport("log", EntryPoint = "__android_log_write", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LogWrite(int prio, string tag, string text);
    public static void Write(string msg) { try { LogWrite(4 /* INFO */, "StackS", msg); } catch { } }
}
