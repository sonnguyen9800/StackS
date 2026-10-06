using System.Text.Json.Nodes;
using StackS.Kernel;

int passed = 0, failed = 0;
void Test(string name, Action body)
{
    try { body(); passed++; Console.WriteLine("  pass  " + name); }
    catch (Exception e) { failed++; Console.WriteLine("  FAIL  " + name + "\n        " + e.Message); }
}
void Assert(bool cond, string msg) { if (!cond) throw new Exception(msg); }
JsonObject Res(string json) => (JsonObject)JsonNode.Parse(json)!;
bool Ok(string json) => Res(json)["ok"]!.GetValue<bool>();

string root = FindRoot();
Console.WriteLine("StackS kernel tests");

Test("player settles on the ground when idle", () =>
{
    var s = Sim.Create(Levels.Default());
    for (int i = 0; i < 60; i++) Sim.Step(s, 0);
    Assert(s.Grounded, "not grounded");
    Assert(Math.Abs(s.Y - 0.45) < 0.001, "y = " + s.Y);
});

Test("jump reaches close to the tuned height", () =>
{
    var s = Sim.Create(Levels.Default());
    for (int i = 0; i < 5; i++) Sim.Step(s, 0);
    double max = s.Y;
    for (int i = 0; i < 45; i++) { Sim.Step(s, Sim.Jump); max = Math.Max(max, s.Y); }
    double rise = max - 0.45;
    Assert(rise > 2.0 && rise < 2.25, "rise = " + rise);
});

Test("same inputs give a bit-identical end state", () =>
{
    var inputs = Enumerable.Range(0, 900).Select(i => (i % 7 != 0 ? 2 : 0) | (i % 50 < 10 ? 16 : 0) | (i % 90 < 45 ? 4 : 8)).ToList();
    var a = Sim.Replay(Levels.Default(), inputs, new List<TuneEvent>());
    var b = Sim.Replay(Levels.Default(), inputs, new List<TuneEvent>());
    Assert(Sim.StateKey(a) == Sim.StateKey(b), "keys differ");
});

foreach (var file in Directory.GetFiles(Path.Combine(root, "content", "replays"), "*.replay.json").OrderBy(f => f))
{
    Test("golden replay matches bit-for-bit: " + Path.GetFileName(file), () =>
    {
        var r = LevelSerializer.LoadReplay(File.ReadAllText(file));
        var s = Sim.Replay(r.Level, r.Inputs, r.Tune);
        var key = Sim.StateKey(s);
        Assert(key == r.ExpectedKey, "\n        expected " + r.ExpectedKey + "\n        got      " + key);
    });
}

Test("Step allocates nothing per tick", () =>
{
    var s = Sim.Create(Levels.Default());
    for (int i = 0; i < 120; i++) Sim.Step(s, 0);   // warm up
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 600; i++) Sim.Step(s, i % 30 < 15 ? Sim.Jump : 0);
    long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
    Assert(bytes == 0, bytes + " bytes allocated");
});

Test("undo and redo restore exact state; consecutive moves merge", () =>
{
    var rt = new Runtime();
    string Parts() => string.Join(";", rt.Doc.Parts.Select(LevelSerializer.PartJson));
    string before = Parts();   // ids are never reused, so nextId is expected to stay advanced
    var id = Res(rt.Exec("part.add", "{\"type\":\"solid\",\"x\":2,\"y\":1,\"z\":2}"))["res"]!.GetValue<string>();
    for (int i = 0; i < 10; i++) rt.Exec("part.move", "{\"id\":\"" + id + "\",\"dx\":0.5}");
    var p = rt.Doc.Parts.Find(q => q.Id == id)!;
    Assert(p.X == 7, "x = " + p.X);
    rt.Exec("undo", null);                 // undoes all 10 merged moves
    Assert(p.X == 2, "after one undo x = " + p.X);
    rt.Exec("undo", null);                 // undoes the add
    Assert(Parts() == before, "parts not restored");
    rt.Exec("redo", null); rt.Exec("redo", null);
    Assert(rt.Doc.Parts.Find(q => q.Id == id)!.X == 7, "redo failed");
});

Test("play never mutates the level document", () =>
{
    var rt = new Runtime();
    string before = LevelSerializer.SaveCompact(rt.Doc);
    rt.Exec("play", null);
    for (int i = 0; i < 300; i++) rt.Tick(i % 20 < 10 ? 2 | 16 : 4);
    Assert(!Ok(rt.Exec("part.add", "{\"type\":\"coin\"}")), "editing during play should fail");
    rt.Exec("stop", null);
    Assert(LevelSerializer.SaveCompact(rt.Doc) == before, "document changed by play");
});

Test("live tuning during play is recorded and the replay still matches", () =>
{
    var rt = new Runtime();
    rt.Exec("play", null);
    for (int i = 0; i < 400; i++)
    {
        if (i == 120) rt.Exec("tune.set", "{\"key\":\"jumpHeight\",\"value\":3.5}");
        if (i == 250) rt.Exec("tune.set", "{\"key\":\"moveSpeed\",\"value\":10}");
        rt.Tick(i % 40 < 20 ? 2 | 16 : 4 | 16);
    }
    rt.Exec("stop", null);
    var v = Res(rt.Exec("replay.verify", null))["res"]!;
    Assert(v["match"]!.GetValue<bool>(), "replay mismatch");
});

Test("save/load round-trips and is stable", () =>
{
    var json = LevelSerializer.Save(Levels.Default());
    var again = LevelSerializer.Save(LevelSerializer.Load(json));
    Assert(json == again, "not stable");
});

Test("loads the JS-written starter level identically", () =>
{
    var d = LevelSerializer.Load(File.ReadAllText(Path.Combine(root, "content", "levels", "starter.level.json")));
    Assert(LevelSerializer.SaveCompact(d) == LevelSerializer.SaveCompact(Levels.Default()), "C# and JS default levels differ");
});

Test("migrates a version 0 file (tune -> tuning)", () =>
{
    var v0 = "{\"tune\":{\"moveSpeed\":8,\"jumpHeight\":3,\"timeToApex\":0.4,\"coyoteTicks\":4},\"parts\":[{\"id\":\"p7\",\"type\":\"solid\",\"x\":0,\"y\":0,\"z\":0,\"w\":1,\"h\":1,\"d\":1}]}";
    var d = LevelSerializer.Load(v0);
    Assert(d.Version == 1 && d.Tuning.MoveSpeed == 8 && d.NextId == 8, "migration failed");
});

Test("bad input is rejected with a clear error", () =>
{
    var rt = new Runtime();
    var r = Res(rt.Exec("part.add", "{\"type\":\"dragon\"}"));
    Assert(!r["ok"]!.GetValue<bool>() && r["err"]!.GetValue<string>().Contains("unknown type"), "wrong error");
    Assert(!Ok(rt.Exec("nope", null)), "unknown command accepted");
});

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

static string FindRoot()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d != null && !Directory.Exists(Path.Combine(d.FullName, "content"))) d = d.Parent;
    return d?.FullName ?? throw new Exception("repo root (with content/) not found");
}
