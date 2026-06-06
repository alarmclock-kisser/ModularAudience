using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModularAudience.Audio;

namespace ModularAudience.Audio.Processors_V3
{
    // When triggered, tries to sync multiple audio playbacks (match on beat, given compatible BPMs) by pausing/playing them precisely as needed
    public class PausingPlaybackSyncer
    {
        private readonly List<AudioObj> tracks;
        private readonly CancellationToken token;
        private readonly double intervalSeconds;
        private readonly int grain;
        private readonly Dictionary<AudioObj, bool> initialPlayingState;
        private readonly Task loopTask;

        // Expose completion task so callers can await the syncer's lifecycle
        public Task Completion => this.loopTask;

        public PausingPlaybackSyncer(IEnumerable<AudioObj> tracks, CancellationToken cancellationToken, double frequency = 0.1, int grain = 10)
        {
            this.tracks = tracks?.ToList() ?? [];
            this.token = cancellationToken;
            this.intervalSeconds = Math.Max(0.02, frequency);
            this.grain = Math.Max(1, grain);
            this.initialPlayingState = this.tracks.ToDictionary(t => t, t => t?.PlayerPlaying == true);

            this.loopTask = Task.Run(this.SyncLoopAsync, cancellationToken);
        }

        public static async Task RunForAsync(IEnumerable<AudioObj> tracks, double? syncingDuration = null, double frequency = 0.1, int grain = 10)
        {
            if (tracks == null) return;

            // Determine duration if not provided or invalid
            double duration = syncingDuration.HasValue && syncingDuration.Value > 0 ? syncingDuration.Value : -1.0;
            var bpms = tracks.Where(t => t != null && t.Bpm > 0).Select(t => (double)t.Bpm).ToList();
            if (duration <= 0)
            {
                if (bpms.Count == 0)
                {
                    duration = 4.0; // default if no BPM info
                }
                else
                {
                    // median bpm
                    bpms.Sort();
                    double median = bpms[bpms.Count / 2];
                    // choose 4 beats as reasonable sync window
                    duration = Math.Max(0.5, 4.0 * (60.0 / median));
                }
            }

            LogCollection.Log($"PausingPlaybackSyncer.RunForAsync: starting syncer for {tracks.Count()} tracks, duration={duration:F2}s, frequency={frequency:F3}, grain={grain}");

            using var cts = new CancellationTokenSource();
            var syncer = new PausingPlaybackSyncer(tracks, cts.Token, frequency, grain);
            try
            {
                // wait for duration
                await Task.Delay(TimeSpan.FromSeconds(duration)).ConfigureAwait(false);
            }
            catch (TaskCanceledException) { }
            finally
            {
                try
                {
                    cts.Cancel();
                }
                catch { }
            }

            try
            {
                await syncer.Completion.ConfigureAwait(false);
            }
            catch { }

            LogCollection.Log("PausingPlaybackSyncer.RunForAsync: syncer finished");
        }

        private async Task SyncLoopAsync()
        {
            try
            {
                while (!this.token.IsCancellationRequested)
                {
                    await this.SyncOnceAsync().ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromSeconds(this.intervalSeconds), this.token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // expected
            }
            finally
            {
                await this.RestoreInitialStatesAsync().ConfigureAwait(false);
            }
        }

        private async Task SyncOnceAsync()
        {
            var playing = this.tracks
                .Where(t => t != null && t.PlayerPlaying && t.Bpm > 0)
                .ToList();

            if (playing.Count < 2)
            {
                return;
            }

            const double bpmTolerance = 0.05; // 5%
            var groups = new List<List<AudioObj>>();
            foreach (var t in playing)
            {
                bool added = false;
                foreach (var g in groups)
                {
                    var refBpm = g[0].Bpm;
                    if (IsCompatibleBpm(t.Bpm, refBpm, bpmTolerance))
                    {
                        g.Add(t);
                        added = true;
                        break;
                    }
                }
                if (!added)
                {
                    groups.Add([t]);
                }
            }

            var bestGroup = groups
                .OrderByDescending(g => g.Count)
                .ThenByDescending(g => g.Average(x => Math.Clamp(x.Volume, 0f, 1f)))
                .FirstOrDefault();

            if (bestGroup == null || bestGroup.Count < 2)
            {
                return;
            }

            var master = bestGroup
                .OrderByDescending(t => Math.Clamp(t.Volume, 0f, 1f))
                .First();

            double masterBeatDur = 60.0 / master.Bpm;
            if (masterBeatDur <= 0)
            {
                return;
            }

            double masterPhase = GetPhase(master, masterBeatDur);
            const double toleranceSeconds = 0.012;
            double pulse = this.intervalSeconds / this.grain;

            foreach (var slave in bestGroup)
            {
                if (ReferenceEquals(slave, master))
                {
                    continue;
                }

                double slaveBeatDur = 60.0 / slave.Bpm;
                if (slaveBeatDur <= 0)
                {
                    continue;
                }

                double slavePhase = GetPhase(slave, slaveBeatDur);
                double diff = WrapPhase(slavePhase - masterPhase, masterBeatDur);

                if (Math.Abs(diff) <= toleranceSeconds)
                {
                    continue;
                }

                if (diff > toleranceSeconds)
                {
                    double pauseMs = Math.Clamp(diff, pulse / 2.0, pulse) * 1000.0;
                    await PulsePauseAsync(slave, (int) pauseMs, this.initialPlayingState).ConfigureAwait(false);
                }
                else if (diff < -toleranceSeconds)
                {
                    if (bestGroup.Count <= 3)
                    {
                        double pauseMs = Math.Clamp(-diff, pulse / 2.0, pulse) * 1000.0;
                        await PulsePauseAsync(master, (int) pauseMs, this.initialPlayingState).ConfigureAwait(false);
                    }
                }
            }
        }

        private static bool IsCompatibleBpm(double a, double b, double tolerance)
        {
            if (b <= 0 || a <= 0)
            {
                return false;
            }
            double ratio = a / b;
            if (Math.Abs(1.0 - ratio) <= tolerance)
            {
                return true;
            }
            double[] allowed = [0.5, 1.5, 2.0, 0.6666667, 1.3333333];
            return allowed.Any(f => Math.Abs(ratio - f) <= tolerance);
        }

        private static double GetPhase(AudioObj track, double beatDuration)
        {
            double t = Math.Max(0, track.CurrentTime.TotalSeconds - Math.Max(0, track.StartingOffset / (double)Math.Max(1, track.Channels) / track.SampleRate));
            double phase = t % beatDuration;
            return phase;
        }

        private static double WrapPhase(double diff, double period)
        {
            double half = period / 2.0;
            while (diff > half)
            {
                diff -= period;
            }

            while (diff < -half)
            {
                diff += period;
            }

            return diff;
        }

        private static async Task PulsePauseAsync(AudioObj track, int pauseMs, Dictionary<AudioObj, bool> initialPlayingState)
        {
            if (pauseMs <= 0 || !track.PlayerPlaying)
            {
                return;
            }
            try
            {
                // Remember whether the track was playing at syncer start; if it wasn't, do not resume it here.
                bool originallyPlaying = initialPlayingState != null && initialPlayingState.TryGetValue(track, out bool was) && was;

                // Pause the track (PauseAsync toggles between pause/resume).
                await track.PauseAsync().ConfigureAwait(false);

                // Short delay to align phase.
                await Task.Delay(pauseMs).ConfigureAwait(false);

                // Resume only if it was originally playing and is still paused.
                if (originallyPlaying && track.Paused)
                {
                    await track.PauseAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                // ignore errors to keep loop alive
            }
        }

        private async Task RestoreInitialStatesAsync()
        {
            foreach (var kvp in this.initialPlayingState)
            {
                var track = kvp.Key;
                bool wasPlaying = kvp.Value;
                if (track == null)
                {
                    continue;
                }

                if (wasPlaying && !track.PlayerPlaying && track.Paused)
                {
                    try { await track.PauseAsync().ConfigureAwait(false); } catch { }
                }
                else if (!wasPlaying && track.PlayerPlaying)
                {
                    try { await track.PauseAsync().ConfigureAwait(false); } catch { }
                }
            }
        }
    }
}
