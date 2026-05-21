using System;
using System.Runtime.CompilerServices;

namespace ModularAudience.Forms.Modules
{
    /// <summary>
    /// Lightweight loudness normalisation helpers for playlist playback.
    /// All operations work in-place on interleaved IEEE-float sample arrays.
    /// </summary>
    internal static class PlaylistNormalizer
    {
        /// <summary>Target RMS amplitude (≈ −14 LUFS, a sensible streaming reference level).</summary>
        private const float TargetRms = 0.20f;

        /// <summary>
        /// Maximum gain that may be applied to a quiet track.
        /// Guards against amplifying near-silence to clipping levels.
        /// </summary>
        private const float MaxGain = 6.0f;

        /// <summary>
        /// Minimum RMS below which we skip normalisation entirely
        /// (effectively silent / noise-floor content).
        /// </summary>
        private const float SilenceThreshold = 0.0001f;

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Measure the RMS amplitude of <paramref name="samples"/>.
        /// Returns 0 for null/empty arrays.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MeasureRms(float[]? samples)
        {
            if (samples == null || samples.Length == 0) return 0f;

            double sum = 0.0;
            for (int i = 0; i < samples.Length; i++)
            {
                double s = samples[i];
                sum += s * s;
            }
            return (float) Math.Sqrt(sum / samples.Length);
        }

        /// <summary>
        /// Scale <paramref name="samples"/> so that their RMS equals <paramref name="targetRms"/>.
        /// Clamps the applied gain to [0, <see cref="MaxGain"/>] and skips near-silent material.
        /// If <paramref name="targetRms"/> is ≤ 0 the method falls back to <see cref="TargetRms"/>.
        /// </summary>
        public static void ApplyRmsGain(float[]? samples, float targetRms)
        {
            if (samples == null || samples.Length == 0) return;

            if (targetRms <= 0f) targetRms = TargetRms;

            float currentRms = MeasureRms(samples);
            if (currentRms < SilenceThreshold) return;

            float gain = Math.Clamp(targetRms / currentRms, 0f, MaxGain);
            if (Math.Abs(gain - 1.0f) < 0.005f) return; // no-op if within 0.5%

            for (int i = 0; i < samples.Length; i++)
                samples[i] *= gain;
        }

        /// <summary>
        /// Normalise <paramref name="samples"/> to <see cref="TargetRms"/> (≈ −14 LUFS).
        /// This overload is used for tracks that are not time-stretched.
        /// </summary>
        public static void NormalizeToTarget(float[]? samples) => ApplyRmsGain(samples, TargetRms);
    }
}
