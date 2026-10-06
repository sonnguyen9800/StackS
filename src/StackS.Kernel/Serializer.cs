using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace StackS.Kernel;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(LevelDoc))]
[JsonSerializable(typeof(ReplayFile))]
internal partial class StackSJson : JsonSerializerContext { }

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(LevelDoc))]
[JsonSerializable(typeof(Part))]
internal partial class StackSJsonCompact : JsonSerializerContext { }

/// <summary>Versioned load/save. Source-generated JSON only (NativeAOT-safe). Old files are migrated forward on load.</summary>
public static class LevelSerializer
{
    public const int CurrentVersion = 1;

    public static string Save(LevelDoc d) => JsonSerializer.Serialize(d, StackSJson.Default.LevelDoc);
    public static string SaveCompact(LevelDoc d) => JsonSerializer.Serialize(d, StackSJsonCompact.Default.LevelDoc);
    public static string PartJson(Part p) => JsonSerializer.Serialize(p, StackSJsonCompact.Default.Part);

    public static LevelDoc Load(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("a level file must be a JSON object");
        return LoadNode(node);
    }

    public static LevelDoc LoadNode(JsonObject node)
    {
        Migrate(node);
        var d = node.Deserialize(StackSJson.Default.LevelDoc) ?? throw new FormatException("empty level file");
        return Validate(d);
    }

    /// <summary>Migration chain. Each step upgrades exactly one version. Add a fixture test for every step.</summary>
    static void Migrate(JsonObject o)
    {
        int v = o["version"] is JsonValue jv && jv.TryGetValue<int>(out var n) ? n : 0;
        if (v == 0)
        {
            // v0 (prototype files) called the tuning block "tune".
            if (o["tune"] is JsonNode t) { o.Remove("tune"); o["tuning"] = t.DeepClone(); }
            v = 1;
        }
        if (v != CurrentVersion) throw new FormatException($"unsupported level version {v}");
        o["version"] = v;
    }

    public static LevelDoc Validate(LevelDoc d)
    {
        foreach (var (key, min, max) in Tuning.Meta)
        {
            var val = d.Tuning.Get(key);
            if (!double.IsFinite(val)) throw new FormatException("tuning " + key + " is not a number");
            d.Tuning.Set(key, Math.Min(max, Math.Max(min, val)));
        }
        int maxId = 0;
        foreach (var p in d.Parts)
        {
            if (PartTypes.Get(p.Type) is null) throw new FormatException("unknown part type " + p.Type);
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z) ||
                !double.IsFinite(p.W) || !double.IsFinite(p.H) || !double.IsFinite(p.D))
                throw new FormatException("non-numeric field on " + p.Id);
            if (p.Id.Length > 1 && int.TryParse(p.Id.AsSpan(1), out var id) && id > maxId) maxId = id;
        }
        d.NextId = maxId + 1;
        d.Version = CurrentVersion;
        return d;
    }

    public static string SaveReplay(ReplayFile r) => JsonSerializer.Serialize(r, StackSJson.Default.ReplayFile);

    public static ReplayFile LoadReplay(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("a replay file must be a JSON object");
        var level = node["level"] as JsonObject ?? throw new FormatException("replay has no level");
        var doc = LoadNode((JsonObject)level.DeepClone());
        node.Remove("level");
        var r = node.Deserialize(StackSJson.Default.ReplayFile) ?? throw new FormatException("empty replay file");
        r.Level = doc;
        return r;
    }
}
