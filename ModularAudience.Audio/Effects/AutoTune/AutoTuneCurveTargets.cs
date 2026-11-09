using ModularAudience.Audio.Effects.Curves;

namespace ModularAudience.Audio.Effects.AutoTune;

/// <summary>Continuous tuning/mix controls. Detector geometry, musical gating duration and FFT topology remain fixed per analysis.</summary>
public static class AutoTuneCurveTargets
{
    public static IReadOnlyList<EffectCurveTarget> All { get; } = Array.AsReadOnly(new EffectCurveTarget[]
    {
        new(EffectCurveTargets.Amount, "Effect amount (%)", 0, 100, 0),
        new(nameof(AutoTuneSettings.IntensityPercent), "Intensity (%)", 0, 100, 0),
        new(nameof(AutoTuneSettings.MaximumCorrectionSemitones), "Maximum correction (semitones)", 0, 24, 0),
        new(nameof(AutoTuneSettings.RetuneMilliseconds), "Retune time (ms)", 0, 2000, 0),
        new(nameof(AutoTuneSettings.ReleaseMilliseconds), "Release time (ms)", 0, 2000, 0),
        new(nameof(AutoTuneSettings.PitchSmoothingMilliseconds), "Note smoothing (ms)", 0, 500, 0),
        new(nameof(AutoTuneSettings.HysteresisCents), "Note-switch hysteresis (cents)", 0, 100, 0),
        new(nameof(AutoTuneSettings.DeadZoneCents), "In-tune dead zone (cents)", 0, 50, 0),
        new(nameof(AutoTuneSettings.VibratoPreservationPercent), "Vibrato preservation (%)", 0, 100, 0),
        new(nameof(AutoTuneSettings.VibratoWindowMilliseconds), "Vibrato window (ms)", 20, 1000, 20),
        new(nameof(AutoTuneSettings.ReferenceA4Hz), "Concert A4 reference (Hz)", 400, 480, 440),
        new(nameof(AutoTuneSettings.FineTuneCents), "Target tuning offset (cents)", -100, 100, 0),
        new(nameof(AutoTuneSettings.MinimumConfidence), "Voicing confidence", 0, 1, 0),
        new(nameof(AutoTuneSettings.SilenceThresholdDb), "Silence gate (dBFS)", -100, 0, -100),
        new(nameof(AutoTuneSettings.VoicingFadeMilliseconds), "Voicing crossfade (ms)", 0, 200, 0),
        new(nameof(AutoTuneSettings.FormantPreservationPercent), "Formant preservation (%)", 0, 100, 0),
        new(nameof(AutoTuneSettings.FormantSmoothingHz), "Formant bandwidth (Hz)", 50, 2500, 50),
        new(nameof(AutoTuneSettings.WetPercent), "Wet / dry (%)", 0, 100, 0),
        new(nameof(AutoTuneSettings.OutputGainDb), "Output gain (dB)", -24, 12, 0)
    });

    public static double BaseValue(AutoTuneSettings settings, string key) => key switch
    {
        EffectCurveTargets.Amount => 100,
        nameof(settings.IntensityPercent) => settings.IntensityPercent,
        nameof(settings.MaximumCorrectionSemitones) => settings.MaximumCorrectionSemitones,
        nameof(settings.RetuneMilliseconds) => settings.RetuneMilliseconds,
        nameof(settings.ReleaseMilliseconds) => settings.ReleaseMilliseconds,
        nameof(settings.PitchSmoothingMilliseconds) => settings.PitchSmoothingMilliseconds,
        nameof(settings.HysteresisCents) => settings.HysteresisCents,
        nameof(settings.DeadZoneCents) => settings.DeadZoneCents,
        nameof(settings.VibratoPreservationPercent) => settings.VibratoPreservationPercent,
        nameof(settings.VibratoWindowMilliseconds) => settings.VibratoWindowMilliseconds,
        nameof(settings.ReferenceA4Hz) => settings.ReferenceA4Hz,
        nameof(settings.FineTuneCents) => settings.FineTuneCents,
        nameof(settings.MinimumConfidence) => settings.MinimumConfidence,
        nameof(settings.SilenceThresholdDb) => settings.SilenceThresholdDb,
        nameof(settings.VoicingFadeMilliseconds) => settings.VoicingFadeMilliseconds,
        nameof(settings.FormantPreservationPercent) => settings.FormantPreservationPercent,
        nameof(settings.FormantSmoothingHz) => settings.FormantSmoothingHz,
        nameof(settings.WetPercent) => settings.WetPercent,
        nameof(settings.OutputGainDb) => settings.OutputGainDb,
        _ => throw new ArgumentException("This Auto Tune option cannot be automated: " + key)
    };
}
