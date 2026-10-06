using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StackS.Kernel;

/// <summary>
/// The runtime's single entry point. Every change to the level, and play/stop, goes through Exec(name, argsJson).
/// The editor, the console and future agent tools all use this same command bus.
/// Results are JSON: {"ok":true,"res":...,"docVersion":n,"mode":"edit"} or {"ok":false,"err":"...",...}.
/// </summary>
public sealed class Runtime
{
    sealed class UndoEntry { public required Action U; public required Action R; public string? K; }

    public sealed class Run
    {
        public required LevelDoc Start;
        public readonly List<int> Inputs = new();
        public readonly List<TuneEvent> Tune = new();
        public string? EndKey;
    }

    LevelDoc doc;
    readonly List<UndoEntry> undo = new(), redo = new();
    SimState? sim;
    Run? run, lastRun;

    public string Mode { get; private set; } = "edit";
    public int DocVersion { get; private set; }
    public LevelDoc Doc => doc;
    public SimState? Sim => sim;
    public Run? LastRun => lastRun;

    public Runtime(LevelDoc? start = null) { doc = start ?? Levels.Default(); }

    void Change(Action u, Action r, string? mergeKey = null)
    {
        var last = undo.Count > 0 ? undo[^1] : null;
        if (mergeKey != null && last != null && last.K == mergeKey) last.R = r;
        else
        {
            undo.Add(new UndoEntry { U = u, R = r, K = mergeKey });
            if (undo.Count > 300) undo.RemoveAt(0);
        }
        redo.Clear();
        DocVersion++;
    }

    void EditOnly() { if (Mode != "edit") throw new InvalidOperationException("stop play first"); }
    Part Find(string id) => doc.Parts.Find(p => p.Id == id) ?? throw new ArgumentException("no part " + id);

    // ---------- argument helpers ----------
    static string Str(JsonObject a, string k) =>
        a[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : a[k]?.ToJsonString() ?? "";

    static double Num(JsonObject a, string k)
    {
        if (a[k] is JsonValue v)
        {
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
        }
        return double.NaN;
    }

    static double OrDefault(double v, double def) => double.IsNaN(v) || v == 0 ? def : v;

    // ---------- JSON result helpers ----------
    static string JStr(string s)
    {
        var buf = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buf)) w.WriteStringValue(s);
        return Encoding.UTF8.GetString(buf.WrittenSpan);
    }

    static string JNum(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    // ---------- the command bus ----------
    public string Exec(string name, string? argsJson)
    {
        bool ok = true; string? res = null, err = null;
        try
        {
            var a = string.IsNullOrWhiteSpace(argsJson) ? new JsonObject() : JsonNode.Parse(argsJson) as JsonObject ?? new JsonObject();
            res = Dispatch(name, a);
        }
        catch (Exception e) { ok = false; err = e.Message; }

        var sb = new StringBuilder();
        sb.Append("{\"ok\":").Append(ok ? "true" : "false");
        if (ok) sb.Append(",\"res\":").Append(res ?? "null");
        else sb.Append(",\"err\":").Append(JStr(err ?? "error"));
        sb.Append(",\"docVersion\":").Append(DocVersion).Append(",\"mode\":").Append(JStr(Mode)).Append('}');
        return sb.ToString();
    }

    string Dispatch(string name, JsonObject a)
    {
        switch (name)
        {
            case "part.add":
            {
                EditOnly();
                var t = PartTypes.Get(Str(a, "type")) ?? throw new ArgumentException("unknown type " + Str(a, "type"));
                var p = new Part
                {
                    Id = "p" + doc.NextId++, Type = t.Key,
                    X = OrDefault(Num(a, "x"), 0), Y = OrDefault(Num(a, "y"), 0), Z = OrDefault(Num(a, "z"), 0),
                    W = OrDefault(Num(a, "w"), t.W), H = OrDefault(Num(a, "h"), t.H), D = OrDefault(Num(a, "d"), t.D),
                };
                var target = doc;
                void Ins() => target.Parts.Add(p);
                void Rem() => target.Parts.Remove(p);
                Ins(); Change(Rem, Ins);
                return JStr(p.Id);
            }
            case "part.set":
            {
                EditOnly();
                var p = Find(Str(a, "id"));
                var f = Str(a, "field");
                if (f == "type")
                {
                    var v = Str(a, "value");
                    if (PartTypes.Get(v) is null) throw new ArgumentException("unknown type " + v);
                    var prev = p.Type; if (prev == v) return JStr(p.Id);
                    p.Type = v; Change(() => p.Type = prev, () => p.Type = v, "set:" + p.Id + ":type");
                    return JStr(p.Id);
                }
                double val = Num(a, "value");
                if (!double.IsFinite(val)) throw new ArgumentException(f + " must be a number");
                if ((f == "w" || f == "h" || f == "d") && val < 0.1) val = 0.1;
                Func<double> get; Action<double> set;
                switch (f)
                {
                    case "x": get = () => p.X; set = v => p.X = v; break;
                    case "y": get = () => p.Y; set = v => p.Y = v; break;
                    case "z": get = () => p.Z; set = v => p.Z = v; break;
                    case "w": get = () => p.W; set = v => p.W = v; break;
                    case "h": get = () => p.H; set = v => p.H = v; break;
                    case "d": get = () => p.D; set = v => p.D = v; break;
                    default: throw new ArgumentException("unknown field " + f);
                }
                double old = get(); if (old == val) return JStr(p.Id);
                set(val); Change(() => set(old), () => set(val), "set:" + p.Id + ":" + f);
                return JStr(p.Id);
            }
            case "part.move":
            {
                EditOnly();
                var p = Find(Str(a, "id"));
                double dx = OrDefault(Num(a, "dx"), 0), dy = OrDefault(Num(a, "dy"), 0), dz = OrDefault(Num(a, "dz"), 0);
                var prev = new Vec3(p.X, p.Y, p.Z); var next = new Vec3(p.X + dx, p.Y + dy, p.Z + dz);
                void Set(Vec3 v) { p.X = v.X; p.Y = v.Y; p.Z = v.Z; }
                Set(next); Change(() => Set(prev), () => Set(next), "move:" + p.Id);
                return JStr(p.Id);
            }
            case "part.remove":
            {
                EditOnly();
                var p = Find(Str(a, "id"));
                int i = doc.Parts.IndexOf(p);
                var target = doc;
                void Rem() => target.Parts.Remove(p);
                void Ins() => target.Parts.Insert(Math.Min(i, target.Parts.Count), p);
                Rem(); Change(Ins, Rem);
                return JStr(p.Id);
            }
            case "tune.set":
            {
                var key = Str(a, "key");
                if (!Tuning.TryRange(key, out var min, out var max)) throw new ArgumentException("unknown tunable " + key);
                double v = Num(a, "value");
                if (!double.IsFinite(v)) throw new ArgumentException("value must be a number");
                v = Math.Min(max, Math.Max(min, v));
                double prev = doc.Tuning.Get(key);
                if (prev == v) return JNum(v);
                var target = doc;
                void Set(double x) { target.Tuning.Set(key, x); sim?.Tuning.Set(key, x); }
                Set(v);
                if (run != null && sim != null) run.Tune.Add(new TuneEvent { Tick = sim.Tick, Key = key, Value = v });
                Change(() => Set(prev), () => Set(v), "tune:" + key);
                return JNum(v);
            }
            case "doc.load":
            {
                EditOnly();
                var node = a["doc"] as JsonObject ?? throw new ArgumentException("missing doc");
                var d = LevelSerializer.LoadNode((JsonObject)node.DeepClone());
                var prev = doc;
                doc = d; Change(() => doc = prev, () => doc = d);
                return JStr(d.Parts.Count + " parts");
            }
            case "doc.get": return LevelSerializer.SaveCompact(doc);
            case "part.get": return LevelSerializer.PartJson(Find(Str(a, "id")));
            case "undo":
            {
                EditOnly();
                if (undo.Count == 0) return JStr("nothing to undo");
                var e = undo[^1]; undo.RemoveAt(undo.Count - 1); e.U(); redo.Add(e); DocVersion++;
                return JStr("undone");
            }
            case "redo":
            {
                EditOnly();
                if (redo.Count == 0) return JStr("nothing to redo");
                var e = redo[^1]; redo.RemoveAt(redo.Count - 1); e.R(); undo.Add(e); DocVersion++;
                return JStr("redone");
            }
            case "play":
            {
                if (Mode == "play") return JStr("already playing");
                var start = doc.Clone();
                Mode = "play"; sim = Kernel.Sim.Create(start); run = new Run { Start = start };
                return JStr("playing");
            }
            case "stop":
            {
                if (Mode != "play" || run == null || sim == null) return JStr("not playing");
                run.EndKey = Kernel.Sim.StateKey(sim); lastRun = run;
                Mode = "edit"; sim = null; run = null;
                return JStr("stopped after " + lastRun.Inputs.Count + " ticks");
            }
            case "tick": Tick((int)OrDefault(Num(a, "input"), 0)); return "null";
            case "replay.verify":
            {
                if (lastRun?.EndKey == null) throw new InvalidOperationException("play the level and stop first");
                var sw = Stopwatch.StartNew();
                var s = Kernel.Sim.Replay(lastRun.Start, lastRun.Inputs, lastRun.Tune);
                var key = Kernel.Sim.StateKey(s);
                double ms = Math.Round(sw.Elapsed.TotalMilliseconds * 10) / 10;
                return "{\"ticks\":" + lastRun.Inputs.Count + ",\"live\":" + JStr(Kernel.Sim.Fnv(lastRun.EndKey)) +
                       ",\"replay\":" + JStr(Kernel.Sim.Fnv(key)) + ",\"match\":" + (key == lastRun.EndKey ? "true" : "false") +
                       ",\"ms\":" + JNum(ms) + ",\"runtime\":\"csharp\"}";
            }
            case "replay.export":
            {
                if (lastRun?.EndKey == null) throw new InvalidOperationException("play the level and stop first");
                var r = new ReplayFile
                {
                    Name = Str(a, "name"), Level = lastRun.Start, Inputs = lastRun.Inputs.ToList(),
                    Tune = lastRun.Tune.ToList(), ExpectedKey = lastRun.EndKey,
                };
                return LevelSerializer.SaveReplay(r);
            }
            default: throw new ArgumentException("unknown command " + name);
        }
    }

    /// <summary>Fast path used every simulation tick (no JSON).</summary>
    public void Tick(int input)
    {
        if (Mode != "play" || run == null || sim == null) return;
        run.Inputs.Add(input);
        Kernel.Sim.Step(sim, input);
    }

    /// <summary>Compact per-frame state for the renderer.</summary>
    public string ViewJson()
    {
        var buf = new ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buf))
        {
            w.WriteStartObject();
            w.WriteString("mode", Mode);
            w.WriteNumber("docVersion", DocVersion);
            if (sim == null) w.WriteNull("sim");
            else
            {
                w.WriteStartObject("sim");
                w.WriteNumber("x", sim.X); w.WriteNumber("y", sim.Y); w.WriteNumber("z", sim.Z);
                w.WriteNumber("vx", sim.Vx); w.WriteNumber("vz", sim.Vz);
                w.WriteBoolean("grounded", sim.Grounded);
                w.WriteStartArray("coins"); foreach (var c in sim.Coins) w.WriteStringValue(c); w.WriteEndArray();
                w.WriteNumber("deaths", sim.Deaths); w.WriteNumber("tick", sim.Tick); w.WriteBoolean("done", sim.Done);
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buf.WrittenSpan);
    }
}
