using System.Diagnostics;
using StackS.Kernel;

// Native headless host for the StackS runtime.
//   stacks verify <file-or-folder>...   re-simulate golden replays, exit 1 on any mismatch
//   stacks bench [ticks]                 simulation speed on the starter level
if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
{
    Console.WriteLine("usage:\n  stacks verify <replay.json | folder>...\n  stacks bench [ticks]");
    return 0;
}

switch (args[0])
{
    case "verify":
    {
        List<string> files = args.Skip(1).SelectMany(a => Directory.Exists(a)
            ? Directory.GetFiles(a, "*.replay.json").OrderBy(f => f).ToArray()
            : new[] { a }).ToList();
        if (files.Count == 0) { Console.Error.WriteLine("no replay files given"); return 2; }
        int bad = 0;
        foreach (var f in files)
        {
            var r = LevelSerializer.LoadReplay(File.ReadAllText(f));
            var sw = Stopwatch.StartNew();
            var s = Sim.Replay(r.Level, r.Inputs, r.Tune);
            var key = Sim.StateKey(s);
            bool ok = key == r.ExpectedKey;
            if (!ok) bad++;
            Console.WriteLine($"{(ok ? "match   " : "MISMATCH")} {Path.GetFileName(f)}  {r.Inputs.Count} ticks  {sw.Elapsed.TotalMilliseconds:F1} ms  {Sim.Fnv(key)}");
        }
        Console.WriteLine(bad == 0 ? $"all {files.Count} replays match" : $"{bad} of {files.Count} replays differ");
        return bad == 0 ? 0 : 1;
    }
    case "bench":
    {
        int ticks = args.Length > 1 && int.TryParse(args[1], out var t) ? t : 1_000_000;
        var s = Sim.Create(Levels.Default());
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < ticks; i++) Sim.Step(s, (i % 97 < 50 ? Sim.Right : Sim.Left) | (i % 61 < 8 ? Sim.Jump : 0) | (i % 131 < 60 ? Sim.Forward : Sim.Back));
        double sec = sw.Elapsed.TotalSeconds;
        Console.WriteLine($"{ticks} ticks in {sec * 1000:F0} ms = {ticks / sec / 60:F0}x real time at 60 Hz");
        return 0;
    }
    default:
        Console.Error.WriteLine("unknown command " + args[0]);
        return 2;
}
