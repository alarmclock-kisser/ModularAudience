using System.Text.Json.Serialization;

namespace ModularAudience.Audio.Effects.AutoTune;

public enum AutoTuneScale { Chromatic, Major, NaturalMinor, HarmonicMinor, MelodicMinor, MajorPentatonic, MinorPentatonic, Blues, Custom }
public enum AutoTuneAnalysisChannel { Strongest, Average, Left, Right }

/// <summary>Immutable, portable parameters for offline monophonic pitch correction.</summary>
public sealed record AutoTuneSettings
{
    public static IReadOnlyList<string> NoteLengths { get; } = Array.AsReadOnly(new[]
        { "1/128", "1/64", "1/32", "1/16", "1/8", "1/4", "1/2", "1", "2", "3", "4" });
    public static IReadOnlyList<int> FftSizes { get; } = Array.AsReadOnly(new[] { 512, 1024, 2048, 4096, 8192 });
    public static IReadOnlyList<int> Overlaps { get; } = Array.AsReadOnly(new[] { 4, 8, 16 });

    public AutoTuneScale Scale { get; init; } = AutoTuneScale.Chromatic;
    public int RootNote { get; init; }
    public int CustomNotesMask { get; init; } = 4095;
    public double IntensityPercent { get; init; } = 100;
    public string MinimumNoteLength { get; init; } = "1/64";
    public double TempoBpm { get; init; } = 120;
    public double MaximumCorrectionSemitones { get; init; } = 2;
    public double RetuneMilliseconds { get; init; } = 50;
    public double ReleaseMilliseconds { get; init; } = 80;
    public double PitchSmoothingMilliseconds { get; init; } = 20;
    public double HysteresisCents { get; init; } = 15;
    public double DeadZoneCents { get; init; } = 2;
    public double VibratoPreservationPercent { get; init; } = 20;
    public double VibratoWindowMilliseconds { get; init; } = 120;
    public double ReferenceA4Hz { get; init; } = 440;
    public double FineTuneCents { get; init; }
    public double MinimumFrequencyHz { get; init; } = 65;
    public double MaximumFrequencyHz { get; init; } = 1200;
    public double MinimumConfidence { get; init; } = 0.6;
    public double SilenceThresholdDb { get; init; } = -55;
    public double AnalysisHopMilliseconds { get; init; } = 10;
    public AutoTuneAnalysisChannel AnalysisChannel { get; init; } = AutoTuneAnalysisChannel.Strongest;
    public int FftSize { get; init; } = 2048;
    public int Overlap { get; init; } = 4;
    public double VoicingFadeMilliseconds { get; init; } = 10;
    public double FormantPreservationPercent { get; init; }
    public double FormantSmoothingHz { get; init; } = 400;
    public double WetPercent { get; init; } = 100;
    public double OutputGainDb { get; init; }
    public double BoundaryFadeMilliseconds { get; init; } = 5;
    public bool ProtectPeak { get; init; } = true;

    [JsonIgnore]
    public double MinimumNoteSeconds
    {
        get
        {
            double wholeNotes = MinimumNoteLength switch
            {
                "1/128" => 1d / 128, "1/64" => 1d / 64, "1/32" => 1d / 32,
                "1/16" => 1d / 16, "1/8" => 1d / 8, "1/4" => 1d / 4,
                "1/2" => 0.5, "1" => 1, "2" => 2, "3" => 3, "4" => 4,
                _ => throw new ArgumentException("Choose a supported minimum note length.")
            };
            return wholeNotes * 4 * 60 / TempoBpm;
        }
    }

    public void Validate(int? sampleRate = null)
    {
        if (!Enum.IsDefined(Scale) || !Enum.IsDefined(AnalysisChannel)) throw new ArgumentException("Invalid Auto Tune mode.");
        if (RootNote is < 0 or > 11) throw new ArgumentOutOfRangeException(nameof(RootNote));
        if (CustomNotesMask is < 0 or > 4095 || (Scale == AutoTuneScale.Custom && CustomNotesMask == 0))
            throw new ArgumentException("A custom scale must contain at least one of the twelve pitch classes.");
        if (!NoteLengths.Contains(MinimumNoteLength)) throw new ArgumentException("Choose a supported minimum note length.");
        Check(IntensityPercent, 0, 100, nameof(IntensityPercent));
        Check(TempoBpm, 20, 400, nameof(TempoBpm));
        Check(MaximumCorrectionSemitones, 0, 24, nameof(MaximumCorrectionSemitones));
        Check(RetuneMilliseconds, 0, 2000, nameof(RetuneMilliseconds));
        Check(ReleaseMilliseconds, 0, 2000, nameof(ReleaseMilliseconds));
        Check(PitchSmoothingMilliseconds, 0, 500, nameof(PitchSmoothingMilliseconds));
        Check(HysteresisCents, 0, 100, nameof(HysteresisCents));
        Check(DeadZoneCents, 0, 50, nameof(DeadZoneCents));
        Check(VibratoPreservationPercent, 0, 100, nameof(VibratoPreservationPercent));
        Check(VibratoWindowMilliseconds, 20, 1000, nameof(VibratoWindowMilliseconds));
        Check(ReferenceA4Hz, 400, 480, nameof(ReferenceA4Hz));
        Check(FineTuneCents, -100, 100, nameof(FineTuneCents));
        Check(MinimumFrequencyHz, 20, 2000, nameof(MinimumFrequencyHz));
        Check(MaximumFrequencyHz, 21, 5000, nameof(MaximumFrequencyHz));
        if (MaximumFrequencyHz <= MinimumFrequencyHz) throw new ArgumentException("Maximum detection frequency must exceed the minimum.");
        if (sampleRate is <= 0 || (sampleRate.HasValue && MaximumFrequencyHz >= sampleRate.Value / 2d))
            throw new ArgumentException("Detection frequencies must be below half the source sample rate (Nyquist).");
        Check(MinimumConfidence, 0, 1, nameof(MinimumConfidence));
        Check(SilenceThresholdDb, -100, 0, nameof(SilenceThresholdDb));
        Check(AnalysisHopMilliseconds, 5, 50, nameof(AnalysisHopMilliseconds));
        if (!FftSizes.Contains(FftSize) || !Overlaps.Contains(Overlap)) throw new ArgumentException("Choose a supported FFT size and overlap.");
        Check(VoicingFadeMilliseconds, 0, 200, nameof(VoicingFadeMilliseconds));
        Check(FormantPreservationPercent, 0, 100, nameof(FormantPreservationPercent));
        Check(FormantSmoothingHz, 50, 2500, nameof(FormantSmoothingHz));
        Check(WetPercent, 0, 100, nameof(WetPercent));
        Check(OutputGainDb, -24, 12, nameof(OutputGainDb));
        Check(BoundaryFadeMilliseconds, 0, 100, nameof(BoundaryFadeMilliseconds));
    }

    private static void Check(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(name, $"{name} must be finite and between {minimum} and {maximum}.");
    }
}
