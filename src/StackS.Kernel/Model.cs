namespace StackS.Kernel;

/// <summary>One kind of part a level can contain. Default size is used when a part is added without one.</summary>
public sealed record PartType(string Key, uint Color, bool Solid, double W, double H, double D, string Label);

public static class PartTypes
{
    public static readonly PartType[] All =
    {
        new("solid",      0xb07a48, true,  2,   1,   2,   "Solid block"),
        new("spawn",      0x4a7bd0, false, 1,   0.2, 1,   "Spawn point"),
        new("checkpoint", 0x3bb6c4, false, 1,   0.2, 1,   "Checkpoint"),
        new("coin",       0xf2c230, false, 0.5, 0.5, 0.5, "Coin"),
        new("goal",       0x5fd068, false, 1,   2,   1,   "Goal"),
        new("kill",       0xd9483b, false, 2,   0.4, 2,   "Kill zone"),
    };

    public static PartType? Get(string? key)
    {
        foreach (var t in All) if (t.Key == key) return t;
        return null;
    }
}

/// <summary>Designer-tunable values. Keys match the JSON names used by the editor.</summary>
public sealed class Tuning
{
    public double MoveSpeed { get; set; } = 6;
    public double JumpHeight { get; set; } = 2.2;
    public double TimeToApex { get; set; } = 0.38;
    public double CoyoteTicks { get; set; } = 6;

    public static readonly (string Key, double Min, double Max)[] Meta =
    {
        ("moveSpeed", 2, 12), ("jumpHeight", 0.5, 5), ("timeToApex", 0.2, 0.8), ("coyoteTicks", 0, 15),
    };

    public Tuning Clone() => (Tuning)MemberwiseClone();

    public static bool TryRange(string key, out double min, out double max)
    {
        foreach (var m in Meta) if (m.Key == key) { min = m.Min; max = m.Max; return true; }
        min = max = 0; return false;
    }

    public double Get(string key) => key switch
    {
        "moveSpeed" => MoveSpeed, "jumpHeight" => JumpHeight, "timeToApex" => TimeToApex, "coyoteTicks" => CoyoteTicks,
        _ => throw new ArgumentException("unknown tunable " + key),
    };

    public void Set(string key, double v)
    {
        switch (key)
        {
            case "moveSpeed": MoveSpeed = v; break;
            case "jumpHeight": JumpHeight = v; break;
            case "timeToApex": TimeToApex = v; break;
            case "coyoteTicks": CoyoteTicks = v; break;
            default: throw new ArgumentException("unknown tunable " + key);
        }
    }
}

/// <summary>A box-shaped part. Plain data only: no behaviour lives here.</summary>
public sealed class Part
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "solid";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double W { get; set; } = 1;
    public double H { get; set; } = 1;
    public double D { get; set; } = 1;
    public Part Clone() => (Part)MemberwiseClone();
}

/// <summary>The level document: what designers author and what is saved to git.</summary>
public sealed class LevelDoc
{
    public int Version { get; set; } = LevelSerializer.CurrentVersion;
    public int NextId { get; set; } = 1;
    public Tuning Tuning { get; set; } = new();
    public List<Part> Parts { get; set; } = new();

    public LevelDoc Clone() => new()
    {
        Version = Version, NextId = NextId, Tuning = Tuning.Clone(),
        Parts = Parts.Select(p => p.Clone()).ToList(),
    };
}

public sealed class TuneEvent
{
    public int Tick { get; set; }
    public string Key { get; set; } = "";
    public double Value { get; set; }
}

/// <summary>A recorded run: the level it started from, every input, every live tuning change, and the expected end state.</summary>
public sealed class ReplayFile
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public LevelDoc Level { get; set; } = new();
    public List<int> Inputs { get; set; } = new();
    public List<TuneEvent> Tune { get; set; } = new();
    public string ExpectedKey { get; set; } = "";
}

public static class Levels
{
    /// <summary>The starter level. Must stay identical to defaultLevel() in editor/sim.js.</summary>
    public static LevelDoc Default()
    {
        var d = new LevelDoc();
        void P(string type, double x, double y, double z, double? w = null, double? h = null, double? dd = null)
        {
            var t = PartTypes.Get(type)!;
            d.Parts.Add(new Part { Id = "p" + d.NextId++, Type = type, X = x, Y = y, Z = z, W = w ?? t.W, H = h ?? t.H, D = dd ?? t.D });
        }
        P("solid", 0, -0.5, 0, 16, 1, 16);
        P("spawn", -6, 0.1, 6);
        P("coin", -4, 0.75, 6);
        P("solid", -3, 0.5, 2); P("coin", -3, 1.6, 2);
        P("solid", 0, 1.5, -1); P("checkpoint", 0, 2.1, -1); P("coin", 0, 2.9, -1);
        P("kill", 3.5, 0.2, 2.5, 3, 0.4, 3);
        P("solid", 3, 2.5, -4); P("coin", 3, 3.6, -4);
        P("solid", 6, 3.5, -6.5); P("coin", 4.5, 4.6, -5.25);
        P("goal", 6, 5, -6.5);
        return d;
    }
}
