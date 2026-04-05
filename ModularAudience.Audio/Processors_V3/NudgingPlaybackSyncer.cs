using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processors_V3
{
	// When triggered, aligns multiple audio playbacks (beat-matching) by subtly adjusting their PlaybackRate (Nudging/Pitch-Bending) rather than destructive pausing.
	// The implementation below follows the high-level guidance in the comments while keeping the logic lightweight and non-destructive.
	public class NudgingPlaybackSyncer
	{
		private readonly List<AudioObj> tracks;
		private readonly CancellationToken token;
		private readonly double intervalSeconds;
		private readonly double maxNudge;
		private readonly Task loopTask;

		public NudgingPlaybackSyncer(IEnumerable<AudioObj> tracks, CancellationToken cancellationToken, double checkInterval = 0.1, double maxNudgeFactor = 0.05)
		{
			this.tracks = tracks?.ToList() ?? [];
			this.token = cancellationToken;
			this.intervalSeconds = Math.Max(0.02, checkInterval);
			this.maxNudge = Math.Clamp(maxNudgeFactor, 0.001, 0.25);

			// start loop immediately
			this.loopTask = Task.Run(this.SyncLoopAsync, cancellationToken);
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
				// expected on cancellation
			}
			finally
			{
				// Smoothly return to 1.0 for all tracks
				await this.SmoothResetRatesAsync().ConfigureAwait(false);
			}
		}

		private async Task SyncOnceAsync()
		{
			// Consider only playing tracks with valid BPM
			var playing = this.tracks
				.Where(t => t != null && t.PlayerPlaying && t.Bpm > 0)
				.ToList();

			if (playing.Count < 2)
			{
				return;
			}

			// Group by compatible BPM (within 5% or simple /2 /1.5 / double relationships)
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
					groups.Add(new List<AudioObj> { t });
				}
			}

			// Pick the largest group; tie-break by average volume
			var bestGroup = groups
				.OrderByDescending(g => g.Count)
				.ThenByDescending(g => g.Average(x => Math.Clamp(x.Volume, 0f, 1f)))
				.FirstOrDefault();

			if (bestGroup == null || bestGroup.Count < 2)
			{
				return;
			}

			// Master = loudest in group
			var master = bestGroup
				.OrderByDescending(t => Math.Clamp(t.Volume, 0f, 1f))
				.First();

			double masterBeatDur = 60.0 / master.Bpm;
			if (masterBeatDur <= 0)
			{
				return;
			}

			double masterPhase = GetPhase(master, masterBeatDur);
			const double toleranceSeconds = 0.010; // 10 ms

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

				// Compute shortest signed phase difference in seconds (slave relative to master)
				double diff = WrapPhase(slavePhase - masterPhase, masterBeatDur);
				if (Math.Abs(diff) <= toleranceSeconds)
				{
					await ApplyRateAsync(slave, 1.0f).ConfigureAwait(false);
					continue;
				}

				// Map diff to rate adjustment. Behind (negative diff) -> speed up; ahead -> slow down.
				double normalized = Math.Clamp(diff / (masterBeatDur / 2.0), -1.0, 1.0);
				double rate = 1.0 - normalized * this.maxNudge;
				rate = Math.Clamp(rate, 1.0 - this.maxNudge, 1.0 + this.maxNudge);

				await ApplyRateAsync(slave, (float) rate).ConfigureAwait(false);
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
			// Allow simple subdivisions / multiples (2x, 0.5x, 1.5x, 3x/2 within tolerance)
			double[] allowed = { 0.5, 1.5, 2.0, 0.6666667, 1.3333333 };
			return allowed.Any(f => Math.Abs(ratio - f) <= tolerance);
		}

		private static double GetPhase(AudioObj track, double beatDuration)
		{
			double t = Math.Max(0, track.CurrentTime.TotalSeconds - Math.Max(0, track.StartingOffset / (double) Math.Max(1, track.Channels) / track.SampleRate));
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

		private static async Task ApplyRateAsync(AudioObj track, float rate)
		{
			// Avoid flooding AdjustSampleRate; only adjust if significantly different
			if (Math.Abs(track.SampleRateFactor - rate) < 0.0005f)
			{
				return;
			}
			track.SampleRateFactor = rate;
			await track.AdjustSampleRate(rate).ConfigureAwait(false);
		}

		private async Task SmoothResetRatesAsync()
		{
			var playable = this.tracks.Where(t => t != null && t.PlayerPlaying).ToList();
			const int steps = 8;
			const int stepDelayMs = 20;
			for (int i = 1; i <= steps; i++)
			{
				float blend = i / (float) steps;
				foreach (var t in playable)
				{
					float rate = (float) (t.SampleRateFactor + (1.0 - t.SampleRateFactor) * blend);
					t.SampleRateFactor = rate;
					try { await t.AdjustSampleRate(rate).ConfigureAwait(false); } catch { }
				}
				await Task.Delay(stepDelayMs).ConfigureAwait(false);
			}
		}
	}
}
