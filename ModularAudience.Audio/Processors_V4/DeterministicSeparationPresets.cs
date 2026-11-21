namespace ModularAudience.Audio.Processors_V4;

public sealed record DeterministicSeparationPresetValues(
    int WindowSize,
    int MaxComponents,
    int Iterations,
    int AnalysisFrames,
    int BlockFrames,
    int MedianFrames,
    int MedianBins,
    double SeparationMargin,
    double MaskFloor,
    double TransientPreservation,
    bool UseCqtAnalysis,
    int CqtBinsPerOctave,
    bool UsePyin,
    bool UseIlrma);

public static class DeterministicSeparationPresets
{
    private static readonly IReadOnlyDictionary<string, DeterministicSeparationPresetValues> Values =
        new Dictionary<string, DeterministicSeparationPresetValues>(StringComparer.Ordinal)
        {
            ["Balanced"] = new(4096, 16, 80, 1024, 128, 17, 17, 2.0, 0.001, 0.75, false, 12, false, false),
            ["Best Quality"] = new(16384, 32, 500, 8192, 512, 65, 65, 1.0, 0.0, 1.0, true, 36, true, false),
            ["High Quality"] = new(8192, 24, 300, 4096, 256, 49, 49, 1.5, 0.0005, 0.95, true, 24, true, false),
            ["Faster"] = new(2048, 12, 40, 512, 64, 9, 9, 3.0, 0.002, 0.60, false, 12, false, false),
            ["Draft"] = new(1024, 8, 20, 256, 32, 5, 5, 4.0, 0.005, 0.50, false, 12, false, false),
            ["DnB / Breaks"] = new(8192, 24, 200, 2048, 256, 33, 17, 2.0, 0.001, 0.95, true, 24, false, false),
            ["Electro / Synth"] = new(8192, 24, 250, 2048, 256, 17, 49, 1.5, 0.0005, 0.85, true, 36, true, false),
            ["Pop / Band"] = new(4096, 20, 150, 2048, 128, 25, 25, 2.0, 0.001, 0.80, false, 12, true, false),
            ["Complex Ensemble"] = new(16384, 32, 400, 4096, 512, 49, 49, 1.0, 0.0, 0.95, true, 36, true, false)
        };

    public static IReadOnlyList<string> Names => Values.Keys.ToArray();

    public static DeterministicSeparationPresetValues Get(string name)
    {
        if (!Values.TryGetValue(name, out DeterministicSeparationPresetValues? preset))
        {
            throw new ArgumentException($"Unknown deterministic separation preset '{name}'.", nameof(name));
        }

        return preset;
    }
}
