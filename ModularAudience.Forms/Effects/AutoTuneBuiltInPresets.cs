using ModularAudience.Audio.Effects.AutoTune;

namespace ModularAudience.Forms.Effects;

internal sealed record AutoTuneBuiltInPreset(string Id, string Name, string Description, AutoTuneSettings Parameters)
{
    internal AutoTunePreset ToPreset() => new(this.Name, this.Parameters, this.Id);
}

/// <summary>Named starting points, not instrument recognition or polyphonic pitch separation.</summary>
internal static class AutoTuneBuiltInPresets
{
    internal static IReadOnlyList<AutoTuneBuiltInPreset> All { get; } = Array.AsReadOnly(new[]
    {
        new AutoTuneBuiltInPreset("soft", "Soft",
            "Subtle correction with a wide in-tune tolerance, slow retuning and preserved vibrato.", new()
            {
                IntensityPercent = 35, RetuneMilliseconds = 150, ReleaseMilliseconds = 190,
                MinimumNoteLength = "1/32", MaximumCorrectionSemitones = 1,
                PitchSmoothingMilliseconds = 45, HysteresisCents = 25, DeadZoneCents = 8,
                VibratoPreservationPercent = 80, VibratoWindowMilliseconds = 180,
                MinimumConfidence = 0.72, SilenceThresholdDb = -50, WetPercent = 85,
                FormantPreservationPercent = 35, Overlap = 8, VoicingFadeMilliseconds = 25, BoundaryFadeMilliseconds = 10
            }),
        new AutoTuneBuiltInPreset("medium", "Medium",
            "Balanced correction: more accurate note centers without immediately flattening all expression.", new()
            {
                IntensityPercent = 65, RetuneMilliseconds = 70, ReleaseMilliseconds = 120,
                MaximumCorrectionSemitones = 2, PitchSmoothingMilliseconds = 25,
                HysteresisCents = 18, DeadZoneCents = 4, VibratoPreservationPercent = 45,
                MinimumConfidence = 0.65, FormantPreservationPercent = 45, Overlap = 8,
                VoicingFadeMilliseconds = 18, BoundaryFadeMilliseconds = 8
            }),
        new AutoTuneBuiltInPreset("strong", "Strong",
            "Fast, firm correction with little vibrato preservation. Use the maximum-adjustment limit to reject wrong notes.", new()
            {
                IntensityPercent = 100, RetuneMilliseconds = 12, ReleaseMilliseconds = 65,
                MinimumNoteLength = "1/128", MaximumCorrectionSemitones = 3,
                PitchSmoothingMilliseconds = 6, HysteresisCents = 6, DeadZoneCents = 0,
                VibratoPreservationPercent = 5, FormantPreservationPercent = 60,
                Overlap = 8, VoicingFadeMilliseconds = 8
            }),
        new AutoTuneBuiltInPreset("vocals-natural", "Vocals - Natural Lead",
            "Gentle lead-vocal stabilization. Keeps vibrato and slides; shorter detected ornaments remain dry.", new()
            {
                IntensityPercent = 70, RetuneMilliseconds = 85, ReleaseMilliseconds = 160,
                MinimumNoteLength = "1/32", MaximumCorrectionSemitones = 1.5,
                MinimumFrequencyHz = 75, MaximumFrequencyHz = 1100, MinimumConfidence = 0.75,
                SilenceThresholdDb = -50, PitchSmoothingMilliseconds = 35, HysteresisCents = 24,
                DeadZoneCents = 6, VibratoPreservationPercent = 85, VibratoWindowMilliseconds = 180,
                FormantPreservationPercent = 80, FormantSmoothingHz = 500,
                FftSize = 4096, Overlap = 8, VoicingFadeMilliseconds = 22, BoundaryFadeMilliseconds = 12
            }),
        new AutoTuneBuiltInPreset("vocals-pop", "Vocals - Pop Tight",
            "Tight modern vocal centers with moderately quick retuning and strong spectral-envelope compensation.", new()
            {
                IntensityPercent = 100, RetuneMilliseconds = 22, ReleaseMilliseconds = 75,
                MaximumCorrectionSemitones = 2, MinimumFrequencyHz = 80, MaximumFrequencyHz = 1400,
                MinimumConfidence = 0.65, SilenceThresholdDb = -50, PitchSmoothingMilliseconds = 12,
                HysteresisCents = 10, DeadZoneCents = 1, VibratoPreservationPercent = 30,
                FormantPreservationPercent = 85, FormantSmoothingHz = 450, Overlap = 8,
                VoicingFadeMilliseconds = 12, BoundaryFadeMilliseconds = 8
            }),
        new AutoTuneBuiltInPreset("vocals-robotic", "Vocals - Hard Robotic",
            "Deliberately audible note snapping: zero retune time, no vibrato preservation, short minimum notes.", new()
            {
                IntensityPercent = 100, RetuneMilliseconds = 0, ReleaseMilliseconds = 20,
                MinimumNoteLength = "1/128", MaximumCorrectionSemitones = 6,
                PitchSmoothingMilliseconds = 0, HysteresisCents = 0, DeadZoneCents = 0,
                VibratoPreservationPercent = 0, VibratoWindowMilliseconds = 80,
                MinimumConfidence = 0.55, FormantPreservationPercent = 30,
                FftSize = 1024, Overlap = 8, VoicingFadeMilliseconds = 3, BoundaryFadeMilliseconds = 3
            }),
        new AutoTuneBuiltInPreset("vocals-low", "Vocals - Low Voice",
            "Lower-register solo voice. Longer windows and conservative confidence gating reduce octave mistakes.", new()
            {
                IntensityPercent = 80, RetuneMilliseconds = 65, ReleaseMilliseconds = 130,
                MinimumNoteLength = "1/32", MaximumCorrectionSemitones = 1.5,
                MinimumFrequencyHz = 50, MaximumFrequencyHz = 500, MinimumConfidence = 0.75,
                SilenceThresholdDb = -50, PitchSmoothingMilliseconds = 30, HysteresisCents = 24,
                DeadZoneCents = 4, VibratoPreservationPercent = 65, VibratoWindowMilliseconds = 180,
                FormantPreservationPercent = 90, FormantSmoothingHz = 300,
                FftSize = 4096, Overlap = 8, VoicingFadeMilliseconds = 25, BoundaryFadeMilliseconds = 10
            }),
        new AutoTuneBuiltInPreset("vocals-high", "Vocals - High Voice",
            "Higher-register solo voice with a wider upper detection range and preserved vibrato.", new()
            {
                IntensityPercent = 75, RetuneMilliseconds = 65, ReleaseMilliseconds = 120,
                MaximumCorrectionSemitones = 1.5, MinimumFrequencyHz = 140, MaximumFrequencyHz = 2200,
                MinimumConfidence = 0.72, PitchSmoothingMilliseconds = 25, HysteresisCents = 22,
                DeadZoneCents = 5, VibratoPreservationPercent = 70, VibratoWindowMilliseconds = 130,
                FormantPreservationPercent = 80, FormantSmoothingHz = 600, Overlap = 8,
                VoicingFadeMilliseconds = 20, BoundaryFadeMilliseconds = 10
            }),
        new AutoTuneBuiltInPreset("instrument-bass", "Instrument - Bass",
            "Sustained monophonic bass, not a complete drum/bass mix. Very low detection uses more CPU and memory.", new()
            {
                IntensityPercent = 90, RetuneMilliseconds = 50, ReleaseMilliseconds = 140,
                MinimumNoteLength = "1/32", MaximumCorrectionSemitones = 1,
                MinimumFrequencyHz = 30, MaximumFrequencyHz = 400, MinimumConfidence = 0.82,
                SilenceThresholdDb = -48, PitchSmoothingMilliseconds = 30, HysteresisCents = 30,
                DeadZoneCents = 4, VibratoPreservationPercent = 50, VibratoWindowMilliseconds = 200,
                FormantPreservationPercent = 30, FormantSmoothingHz = 200,
                FftSize = 8192, Overlap = 8, VoicingFadeMilliseconds = 25, BoundaryFadeMilliseconds = 12
            }),
        new AutoTuneBuiltInPreset("instrument-lead", "Instrument - Guitar Lead",
            "A single melodic guitar/lead line. Preserves bends and vibrato; cautious gating avoids processing the attack.", new()
            {
                IntensityPercent = 65, RetuneMilliseconds = 65, ReleaseMilliseconds = 100,
                MaximumCorrectionSemitones = 1.2, MinimumFrequencyHz = 70, MaximumFrequencyHz = 1500,
                MinimumConfidence = 0.72, SilenceThresholdDb = -45, PitchSmoothingMilliseconds = 25,
                HysteresisCents = 20, DeadZoneCents = 5, VibratoPreservationPercent = 70,
                VibratoWindowMilliseconds = 140, FormantPreservationPercent = 35, Overlap = 8,
                VoicingFadeMilliseconds = 25, BoundaryFadeMilliseconds = 8
            }),
        new AutoTuneBuiltInPreset("instrument-bowed-wind", "Instrument - Bowed / Wind",
            "Slow, expressive correction for a solo bowed string or wind instrument, retaining most vibrato.", new()
            {
                IntensityPercent = 60, RetuneMilliseconds = 110, ReleaseMilliseconds = 180,
                MinimumNoteLength = "1/16", MaximumCorrectionSemitones = 1,
                MinimumFrequencyHz = 100, MaximumFrequencyHz = 2000, MinimumConfidence = 0.78,
                SilenceThresholdDb = -50, PitchSmoothingMilliseconds = 45, HysteresisCents = 30,
                DeadZoneCents = 8, VibratoPreservationPercent = 95, VibratoWindowMilliseconds = 200,
                FormantPreservationPercent = 65, FftSize = 4096, Overlap = 8,
                VoicingFadeMilliseconds = 35, BoundaryFadeMilliseconds = 12
            }),
        new AutoTuneBuiltInPreset("ensemble-unison", "Ensemble - Unison Only",
            "Very conservative shared correction for mostly unison material. NOT independent chord/choir tuning or polyphonic separation; start with dry comparison.", new()
            {
                IntensityPercent = 25, WetPercent = 45, RetuneMilliseconds = 200, ReleaseMilliseconds = 250,
                MinimumNoteLength = "1/16", MaximumCorrectionSemitones = 0.5,
                MinimumFrequencyHz = 80, MaximumFrequencyHz = 1600, MinimumConfidence = 0.9,
                SilenceThresholdDb = -42, PitchSmoothingMilliseconds = 80, HysteresisCents = 40,
                DeadZoneCents = 10, VibratoPreservationPercent = 100, VibratoWindowMilliseconds = 250,
                FormantPreservationPercent = 50, FftSize = 4096, Overlap = 8,
                VoicingFadeMilliseconds = 50, BoundaryFadeMilliseconds = 20
            }),
        new AutoTuneBuiltInPreset("creative-c-minor", "Creative - C Minor Snap",
            "Obvious hard snapping into C natural minor. Change the root/scale to match your song before applying.", new()
            {
                Scale = AutoTuneScale.NaturalMinor, RootNote = 0, IntensityPercent = 100,
                RetuneMilliseconds = 0, ReleaseMilliseconds = 20, MinimumNoteLength = "1/128",
                MaximumCorrectionSemitones = 5, PitchSmoothingMilliseconds = 0,
                HysteresisCents = 0, DeadZoneCents = 0, VibratoPreservationPercent = 0,
                MinimumConfidence = 0.55, FormantPreservationPercent = 0,
                FftSize = 1024, Overlap = 8, VoicingFadeMilliseconds = 3, BoundaryFadeMilliseconds = 3
            })
    });

    internal static string? Description(string? id) => All.FirstOrDefault(preset => preset.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Description;
}
