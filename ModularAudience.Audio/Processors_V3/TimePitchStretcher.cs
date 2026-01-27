using System;
using System.Threading;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processors_V3
{
	// Emits static async methods for time-stretching WITH pitch-shifting for timed mid-playback speed+pitch-adjustments of AudioObj instances by precise factors
	// Given a playing AudioObj, target speed factor (e.g., 1.25 for 25% faster), and duration (e.g., incoming 2,85s), adjusts the playback rate and pitch accordingly over the specified duration
	// Wouldn't it be easiest and fastest to basically adjust the SampleRate of the AudioObj temporarily to achieve this effect, then revert it back to normal after the duration has elapsed?
	// Maybe resample instead because of PlaybackEngine limitations?
	// duration = 0 means until the end of the AudioObj
	// ignore duration for stopped (not-playing) AudioObj's, just set speed/pitch for whole data

	public static class TimePitchStretcher
	{
		/// <summary>
		/// Varispeed (tempo+pitch) adjustment. When playing, adjusts playback SampleRateFactor and optionally reverts after durationSeconds. When not playing, applies factor to SampleRateFactor and updates metadata (Duration, BPM).
		/// </summary>
		public static async Task StretchAsync(AudioObj audioObj, double targetSpeedFactor, double durationSeconds = 0)
		{
			if (audioObj == null)
			{
				throw new ArgumentNullException(nameof(audioObj));
			}

			targetSpeedFactor = targetSpeedFactor <= 0 ? 1.0 : targetSpeedFactor;

			// Fast path: if not playing, set factor and update derived fields; ignore duration as requested
			if (!audioObj.PlayerPlaying)
			{
				audioObj.SampleRateFactor = targetSpeedFactor;
				// Update duration/BPM to reflect new playback rate (varispeed)
				if (audioObj.SampleRate > 0 && audioObj.Channels > 0)
				{
					long frames = audioObj.Length / Math.Max(1, audioObj.Channels);
					audioObj.Duration = TimeSpan.FromSeconds(frames / (audioObj.SampleRate * targetSpeedFactor));
				}
				if (audioObj.Bpm > 0)
				{
					audioObj.Bpm = (float) (audioObj.Bpm * targetSpeedFactor);
				}
				return;
			}

			// Playing: adjust playback rate; optionally revert after durationSeconds
			double originalFactor = audioObj.SampleRateFactor;
			await audioObj.AdjustSampleRate((float) targetSpeedFactor).ConfigureAwait(false);
			audioObj.SampleRateFactor = targetSpeedFactor;

			if (durationSeconds > 0)
			{
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(durationSeconds)).ConfigureAwait(false);
				}
				catch (TaskCanceledException)
				{
					return; // if caller cancelled externally
				}

				// If still playing and factor unchanged, revert
				if (Math.Abs(audioObj.SampleRateFactor - targetSpeedFactor) < 1e-6 && audioObj.PlayerPlaying)
				{
					audioObj.SampleRateFactor = originalFactor;
					await audioObj.AdjustSampleRate((float) originalFactor).ConfigureAwait(false);
				}
			}
		}
	}
}
