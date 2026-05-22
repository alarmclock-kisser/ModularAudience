using System;

namespace ModularAudience.Forms.Modules
{
    /// <summary>
    /// Calculates a beat-aligned crossfade start time so that the incoming track
    /// begins on (or very close to) a beat of the currently playing track.
    ///
    /// Instead of starting the crossfade at the exact configured moment, the engine
    /// may shift the trigger slightly earlier or later — within a tolerance window —
    /// to land on the nearest beat boundary.  If no beat information is available,
    /// or the shift would exceed the tolerance, the nominal trigger time is used as-is.
    /// </summary>
    internal static class OnBeatCrossfadeAligner
    {
        // ── Constants ──────────────────────────────────────────────────────────

        /// <summary>
        /// Hard cap on how many seconds before the nominal trigger we may start early.
        /// </summary>
        private const double MaxEarlySeconds = 4.0;

        /// <summary>
        /// Hard cap on how many seconds after the nominal trigger we may start late.
        /// </summary>
        private const double MaxLateSeconds = 4.0;

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Given the current playback state, compute the beat-aligned crossfade offset.
        /// </summary>
        /// <param name="bpm">BPM of the current (playing) track. 0 = unknown, use nominal.</param>
        /// <param name="currentPosition">Current playback position of the playing track.</param>
        /// <param name="remainingSeconds">Seconds left in the current track.</param>
        /// <param name="nominalCrossfadeSeconds">Configured crossfade overlap (effectiveCrossfade).</param>
        /// <returns>
        /// The adjusted number of seconds before the crossfade should start.
        /// A value of 0 means "start now".  A positive value means "wait this many more seconds".
        /// </returns>
        public static double ComputeWaitSeconds(
            float   bpm,
            TimeSpan currentPosition,
            double  remainingSeconds,
            double  nominalCrossfadeSeconds)
        {
            // How many seconds until the nominal crossfade trigger point?
            double nominalWait = remainingSeconds - nominalCrossfadeSeconds;

            // Immediate trigger (already at or past the nominal point)
            if (nominalWait <= 0.0)
            {
                return 0.0;
            }

            // No BPM → cannot align, use nominal
            if (bpm <= 0f)
            {
                return nominalWait;
            }

            double secondsPerBeat = 60.0 / bpm;

            // Beat phase of the nominal trigger moment (measured from track start)
            double nominalPosition = currentPosition.TotalSeconds + nominalWait;
            double phase           = nominalPosition % secondsPerBeat;

            // Distance to the beat BEFORE and AFTER the nominal trigger point
            double distToPrev = phase;                       // seconds before nominal
            double distToNext = secondsPerBeat - phase;      // seconds after nominal

            // Pick the closer beat
            double beatShift = (distToPrev <= distToNext) ? -distToPrev : distToNext;

            // Clamp to tolerance
            beatShift = Math.Clamp(beatShift, -MaxEarlySeconds, MaxLateSeconds);

            double adjustedWait = nominalWait + beatShift;

            // Never return negative (would mean "start in the past")
            adjustedWait = Math.Max(0.0, adjustedWait);

            return adjustedWait;
        }

        /// <summary>
        /// Returns true when the engine is close enough to the crossfade window that
        /// beat-alignment should be evaluated.  The look-ahead is 2 full beats ahead of
        /// the earliest possible trigger so the engine always has time to compute.
        /// </summary>
        public static bool IsInAlignmentWindow(
            float  bpm,
            double remainingSeconds,
            double nominalCrossfadeSeconds)
        {
            double lookAheadSeconds = bpm > 0f
                ? Math.Min(MaxEarlySeconds + 2.0 * (60.0 / bpm), MaxEarlySeconds + 2.0)
                : MaxEarlySeconds;

            return remainingSeconds <= nominalCrossfadeSeconds + lookAheadSeconds;
        }
    }
}
