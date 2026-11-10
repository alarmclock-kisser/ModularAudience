using System.Text.Json.Serialization;

namespace ModularAudience.Audio.Vocal;

/// <summary>A single configurable emphasis marker, e.g. "*" for strong emphasis.</summary>
public sealed class VocalEmphasisRule
{
    public string Marker { get; set; } = "*";
    public VocalEmphasisMode Mode { get; set; } = VocalEmphasisMode.Strong;
    /// <summary>Numeric payload: pause length in ms, or multiplier for lengthen/pitch.</summary>
    public double Value { get; set; } = 1.0;
    /// <summary>Whether the marker wraps a segment (true) or is an inline token (false).</summary>
    public bool Wrapping { get; set; } = true;

    public VocalEmphasisRule Clone() => new()
    {
        Marker = Marker,
        Mode = Mode,
        Value = Value,
        Wrapping = Wrapping,
    };
}

public sealed class VocalEmphasisSyntax
{
    public List<VocalEmphasisRule> Rules { get; set; } = [];

    public static VocalEmphasisSyntax Default() => new()
    {
        Rules =
        [
            new VocalEmphasisRule { Marker = "*", Mode = VocalEmphasisMode.Strong, Value = 1.35, Wrapping = true },
            new VocalEmphasisRule { Marker = "~", Mode = VocalEmphasisMode.Whisper, Value = 0.55, Wrapping = true },
            new VocalEmphasisRule { Marker = "-", Mode = VocalEmphasisMode.Pause, Value = 120, Wrapping = false },
            new VocalEmphasisRule { Marker = "+", Mode = VocalEmphasisMode.Lengthen, Value = 1.5, Wrapping = false },
            new VocalEmphasisRule { Marker = "...", Mode = VocalEmphasisMode.Pause, Value = 400, Wrapping = false },
            new VocalEmphasisRule { Marker = "#", Mode = VocalEmphasisMode.PitchUp, Value = 1.25, Wrapping = true },
            new VocalEmphasisRule { Marker = "_", Mode = VocalEmphasisMode.Strong, Value = 1.15, Wrapping = true },
        ],
    };

    public VocalEmphasisRule? FindWrapping(string marker) =>
        Rules.FirstOrDefault(r => r.Wrapping && r.Marker == marker);

    public VocalEmphasisRule? FindInline(string marker) =>
        Rules.FirstOrDefault(r => !r.Wrapping && r.Marker == marker);
}