namespace ModularAudience.Forms.Effects;

/// <summary>Track metadata takes priority over a preset's portable tempo starting point.</summary>
internal static class AutoTuneTempoResolver
{
    internal static double Resolve(double bpm, double scannedBpm, double presetBpm = 120)
    {
        if (IsUsable(bpm)) return bpm;
        if (IsUsable(scannedBpm)) return scannedBpm;
        return IsUsable(presetBpm) ? presetBpm : 120;
    }

    private static bool IsUsable(double bpm) => double.IsFinite(bpm) && bpm is >= 20 and <= 400;
}
