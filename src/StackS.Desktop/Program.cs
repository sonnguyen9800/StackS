using StackS.Game;
using StackS.Kernel;

// StackS desktop game.
//   stacks-game [level.json] [--replay run.replay.json] [--record out.replay.json] [--no-ps1] [--ticks N]
// Controls: WASD/arrows move, Space jumps, P toggles the PS1 look, R restarts, Esc quits.
var o = new GameOptions { Log = Console.WriteLine };
string? levelPath = null;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--replay": o.Playback = LevelSerializer.LoadReplay(File.ReadAllText(args[++i])); break;
        case "--record": o.RecordPath = args[++i]; break;
        case "--no-ps1": o.Ps1 = false; break;
        case "--ticks": o.QuitAfterTicks = int.Parse(args[++i]); break;
        case "--help": case "-h":
            Console.WriteLine("stacks-game [level.json] [--replay run.replay.json] [--record out.replay.json] [--no-ps1] [--ticks N]");
            return 0;
        default: levelPath = args[i]; break;
    }
}
if (o.Playback != null) o.Level = o.Playback.Level;
else
{
    levelPath ??= FindContent("current.level.json") ?? FindContent("starter.level.json");
    if (levelPath != null) { o.Level = LevelSerializer.Load(File.ReadAllText(levelPath)); Console.WriteLine("level: " + levelPath); }
}
GameHost.RunDesktop(o);
return 0;

static string? FindContent(string name)
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
    {
        var f = Path.Combine(d.FullName, "content", "levels", name);
        if (File.Exists(f)) return f;
    }
    return null;
}
