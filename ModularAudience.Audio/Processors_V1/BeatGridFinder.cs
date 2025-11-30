using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processors_V1
{
	public static class BeatGridFinder
	{
		public static async Task<TimeSpan> FindSilenceDurationStartAsync(AudioObj audio, float? threshold = null, int? minDurationMs = null)
		{
			if (audio == null || audio.Data == null || audio.Data.Length == 0 || audio.SampleRate <= 0)
			{
				return TimeSpan.Zero;
			}

			return await Task.Run(() => ComputeSilenceDuration(audio, findStart: true, threshold, minDurationMs));
		}

		public static async Task<TimeSpan> FindSilenceDurationEndAsync(AudioObj audio, float? threshold = null, int? minDurationMs = null)
		{
			if (audio == null || audio.Data == null || audio.Data.Length == 0 || audio.SampleRate <= 0)
			{
				return TimeSpan.Zero;
			}

			return await Task.Run(() => ComputeSilenceDuration(audio, findStart: false, threshold, minDurationMs));
		}



		public static async Task TrimSilenceAsync(AudioObj audio, float? threshold = null, int? minDurationMs = null)
		{
			// Find silence start & end
			double startSilenceSeconds = (await FindSilenceDurationStartAsync(audio, threshold, minDurationMs)).TotalSeconds;
			double endSilenceSeconds = (await FindSilenceDurationEndAsync(audio, threshold, minDurationMs)).TotalSeconds;

			LogCollection.Log($"Trimming silence with threshold={(threshold.HasValue ? threshold.Value.ToString("F5") : "auto")}, minDurationMs={(minDurationMs.HasValue ? minDurationMs.Value.ToString() : "default")}");

			// Select samples from seconds
			audio.SelectionStart = 0;
			audio.SelectionEnd = audio.GetSampleAtSeconds(startSilenceSeconds);
			await audio.EraseSelectionAsync();

			audio.SelectionStart = audio.GetSampleAtSeconds(audio.Duration.TotalSeconds - endSilenceSeconds);
			audio.SelectionEnd = audio.Length;
			await audio.EraseSelectionAsync();

			// Clear selection
			audio.SelectionStart = 0;
			audio.SelectionEnd = 0;
		}




		// Gemeinsame Implementierung — liefert TimeSpan der Stille am Anfang bzw. Ende.
		private static TimeSpan ComputeSilenceDuration(AudioObj audio, bool findStart, float? threshold, int? minDurationMs)
		{
			int sampleRate = Math.Max(1, audio.SampleRate);
			int channels = Math.Max(1, audio.Channels <= 0 ? 1 : audio.Channels);
			int minDurMs = minDurationMs ?? 40; // Default 40 ms
			int minDurSamples = (int) Math.Ceiling(sampleRate * (minDurMs / 1000.0));

			// Envelope window/hop für groben Scan
			int windowSamples = Math.Clamp(sampleRate / 44, 256, 4096);
			int hopSamples = Math.Max(1, windowSamples / 4);

			float[] data = audio.Data;
			int totalFrames = data.Length / channels;

			// Mono: max-abs über Kanäle (erhält Transienten)
			float[] mono = new float[totalFrames];
			if (channels == 1)
			{
				for (int i = 0; i < totalFrames; i++) mono[i] = MathF.Abs(data[i]);
			}
			else
			{
				for (int f = 0, di = 0; f < totalFrames; f++)
				{
					float max = 0f;
					for (int c = 0; c < channels; c++, di++)
					{
						float v = data[di];
						float av = v < 0 ? -v : v;
						if (av > max) max = av;
					}
					mono[f] = max;
				}
			}

			// RMS‑ähnliche Hüllkurve (pro Fenster)
			List<float> envelope = new();
			int approxCount = Math.Max(0, (totalFrames - windowSamples) / Math.Max(1, hopSamples) + 1);
			envelope.Capacity = approxCount;
			for (int start = 0; start + windowSamples <= totalFrames; start += hopSamples)
			{
				double sumSq = 0.0;
				int end = start + windowSamples;
				for (int i = start; i < end; i++)
				{
					double v = mono[i];
					sumSq += v * v;
				}
				double rms = Math.Sqrt(sumSq / windowSamples);
				envelope.Add((float) rms);
			}

			// Sehr kurze Audios: fallback sample‑level
			if (envelope.Count == 0)
			{
				if (findStart)
				{
					for (int i = 0; i < mono.Length; i++)
					{
						if (mono[i] > 1e-5f) return TimeSpan.FromSeconds(i / (double) sampleRate);
					}
					return audio.Duration; // komplett stumm
				}
				else
				{
					for (int i = mono.Length - 1; i >= 0; i--)
					{
						if (mono[i] > 1e-5f)
						{
							int trailing = Math.Max(0, totalFrames - 1 - i);
							return TimeSpan.FromSeconds(trailing / (double) sampleRate);
						}
					}
					return audio.Duration;
				}
			}

			float[] envArr = envelope.ToArray();

			// Rauschboden: 10th percentile
			float noiseFloor;
			{
				var sorted = envArr.OrderBy(x => x).ToArray();
				int idx = (int) Math.Floor(sorted.Length * 0.1);
				idx = Math.Clamp(idx, 0, sorted.Length - 1);
				noiseFloor = Math.Max(1e-7f, sorted[idx]);
			}

			// Threshold setzen oder dynamisch bestimmen
			float thresh;
			if (threshold.HasValue)
			{
				thresh = MathF.Max(1e-7f, threshold.Value);
			}
			else
			{
				if (noiseFloor < 1e-4f) thresh = MathF.Max(1e-5f, noiseFloor * 10f);
				else if (noiseFloor < 5e-3f) thresh = noiseFloor * 6f;
				else thresh = noiseFloor * 4f;
				thresh = Math.Clamp(thresh, 1e-5f, 0.3f);
			}

			// Anzahl Fenster, die benötigt werden um minDuration abzudecken
			int neededWindows = Math.Max(1, (int) Math.Ceiling((minDurSamples - windowSamples) / (double) hopSamples) + 1);

			if (findStart)
			{
				// Suche vorwärts nach dem ersten sustain-Event
				int foundWindowIndex = -1;
				for (int i = 0; i < envArr.Length; i++)
				{
					if (envArr[i] >= thresh)
					{
						int run = 1;
						for (int j = i + 1; j < envArr.Length && run < neededWindows; j++, run++)
						{
							if (envArr[j] < thresh) break;
						}
						if (run >= neededWindows)
						{
							foundWindowIndex = i;
							break;
						}
					}
				}

				if (foundWindowIndex < 0)
				{
					// Kein Signal -> komplette Dauer ist Stille
					return audio.Duration;
				}

				// Grobe Startprobe (Samples)
				int coarseSample = foundWindowIndex * hopSamples;

				// Verfeinerung: suche rückwärts nach erstem Sample, das einen niedrigeren refineThresh überschreitet
				float refineThresh = MathF.Max(1e-7f, thresh * 0.5f);
				int refineLookback = Math.Min(coarseSample, windowSamples * 2);
				int refineStart = Math.Max(0, coarseSample - refineLookback);
				int exactSample = -1;
				for (int s = refineStart; s <= coarseSample && s < totalFrames; s++)
				{
					if (mono[s] >= refineThresh)
					{
						exactSample = s;
						break;
					}
				}

				// Falls nicht gefunden, rückwärts scan starten
				if (exactSample < 0)
				{
					for (int s = coarseSample; s >= 0; s--)
					{
						if (mono[s] < refineThresh)
						{
							exactSample = Math.Min(totalFrames - 1, s + 1);
							break;
						}
						if (s == 0) exactSample = 0;
					}
				}

				if (exactSample < 0) exactSample = coarseSample;
				double seconds = exactSample / (double) sampleRate;
				return TimeSpan.FromSeconds(seconds);
			}
			else
			{
				// Suche rückwärts nach dem letzten sustain-Event (Ende des letzten Signals)
				int foundWindowIndex = -1;
				int foundRunLength = 0;

				for (int i = envArr.Length - 1; i >= 0; i--)
				{
					if (envArr[i] >= thresh)
					{
						int run = 1;
						for (int j = i - 1; j >= 0 && run < neededWindows; j--, run++)
						{
							if (envArr[j] < thresh) break;
						}
						if (run >= neededWindows)
						{
							// Startindex der run (vorwärts)
							foundWindowIndex = i - (run - 1);
							foundRunLength = run;
							// erster Treffer beim Rückwärtslauf ist die letzte run nahe Dateiende
							break;
						}
					}
				}

				if (foundWindowIndex < 0)
				{
					// Kein Signal -> komplette Dauer ist Stille
					return audio.Duration;
				}

				// Bestimme lastWindowIndex innerhalb envelope
				int runLen = 0;
				for (int k = foundWindowIndex; k < envArr.Length; k++)
				{
					if (envArr[k] >= thresh) runLen++;
					else break;
				}
				int lastWindowIndex = foundWindowIndex + Math.Max(0, runLen - 1);

				// Grobe Endprobe (letzter Sample innerhalb der letzten Window)
				int coarseEndSample = lastWindowIndex * hopSamples + windowSamples - 1;
				coarseEndSample = Math.Clamp(coarseEndSample, 0, totalFrames - 1);

				// Verfeinerung: suche rückwärts vom coarseEndSample den letzten Sample >= refineThresh
				float refineThresh2 = MathF.Max(1e-7f, thresh * 0.5f);
				int exactLastSound = -1;
				for (int s = coarseEndSample; s >= 0; s--)
				{
					if (mono[s] >= refineThresh2)
					{
						exactLastSound = s;
						break;
					}
				}

				if (exactLastSound < 0)
				{
					// kein Sample gefunden => komplett stumm
					return audio.Duration;
				}

				int trailingSamples = Math.Max(0, totalFrames - 1 - exactLastSound);
				double seconds = trailingSamples / (double) sampleRate;
				return TimeSpan.FromSeconds(seconds);
			}
		}
	}
}