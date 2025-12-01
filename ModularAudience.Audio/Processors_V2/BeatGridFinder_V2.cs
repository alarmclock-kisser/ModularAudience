using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processors_V2
{
	public static class BeatGridFinder_V2
	{
		public static async Task<TimeSpan> FindSilenceDurationStartAsync(AudioObj audio, float? threshold = null, int? minDurationMs = null)
		{
			if (audio == null || audio.Data == null || audio.Data.Length == 0 || audio.SampleRate <= 0)
            {
                return TimeSpan.Zero;
            }

            var result = await Task.Run(() => ComputeSilenceDuration(audio, true, threshold, minDurationMs));
			LogCollection.Log("Detected silence at start: " + result.TotalSeconds.ToString("F1") + " seconds");
			return result;
		}

		public static async Task<TimeSpan> FindSilenceDurationEndAsync(AudioObj audio, float? threshold = null, int? minDurationMs = null)
		{
			if (audio == null || audio.Data == null || audio.Data.Length == 0 || audio.SampleRate <= 0)
            {
                return TimeSpan.Zero;
            }

            var result = await Task.Run(() => ComputeSilenceDuration(audio, false, threshold, minDurationMs));
			LogCollection.Log("Detected silence at end: " + result.TotalSeconds.ToString("F1") + " seconds");
			return result;
		}

		public static async Task TrimSilenceAsync(AudioObj audio, float? threshold = null, int? minDurationMs = null)
		{
			double startSilenceSeconds = (await FindSilenceDurationStartAsync(audio, threshold, minDurationMs)).TotalSeconds;
			double endSilenceSeconds = (await FindSilenceDurationEndAsync(audio, threshold, minDurationMs)).TotalSeconds;

			LogCollection.Log($"Trimming silence - start: {startSilenceSeconds:F2}s, end: {endSilenceSeconds:F2}s, threshold={(threshold.HasValue ? threshold.Value.ToString("F5") : "auto")}, minDurationMs={(minDurationMs.HasValue ? minDurationMs.Value.ToString() : "default")}");

			audio.SelectionStart = 0;
			audio.SelectionEnd = audio.GetSampleAtSeconds(startSilenceSeconds) * audio.Channels;
			await audio.EraseSelectionAsync();

			audio.SelectionStart = audio.GetSampleAtSeconds(audio.Duration.TotalSeconds - endSilenceSeconds) * audio.Channels;
			audio.SelectionEnd = audio.Length;
			await audio.EraseSelectionAsync();

			audio.SelectionStart = 0;
			audio.SelectionEnd = 0;
		}

		private static TimeSpan ComputeSilenceDuration(AudioObj audio, bool findStart, float? threshold, int? minDurationMs)
		{
			int sampleRate = Math.Max(1, audio.SampleRate);
			int channels = Math.Max(1, audio.Channels <= 0 ? 1 : audio.Channels);
			int minDurMs = minDurationMs ?? 80;
			int minDurSamples = (int) Math.Ceiling(sampleRate * (minDurMs / 1000.0));

			float[] data = audio.Data ?? Array.Empty<float>();
			int totalFrames = Math.Max(0, data.Length / channels);
			if (totalFrames <= 0)
            {
                return TimeSpan.Zero;
            }

            int downsampleFactor = Math.Max(1, sampleRate / 1000);
			int envLen = Math.Max(1, totalFrames / downsampleFactor);
			var env = new float[envLen];

            Parallel.For(0, envLen, idx =>
			{
				int frameStart = Math.Min(totalFrames - 1, idx * downsampleFactor);
				int frameEnd = Math.Min(totalFrames - 1, frameStart + downsampleFactor - 1);
				double sumSq = 0;
				int count = 0;

				for (int f = frameStart; f <= frameEnd; f++)
				{
					int di = f * channels;
					for (int c = 0; c < channels; c++)
					{
						float v = data[di + c];
						sumSq += v * v;
						count++;
					}
				}

				env[idx] = (float) Math.Sqrt(sumSq / Math.Max(1, count));
			});

			int smoothWin = Math.Max(3, Math.Min(21, envLen / 200));
			env = MovingAverage(env, smoothWin);

			var sortedEnv = env.OrderBy(x => x).ToArray();
			int n = sortedEnv.Length;
			float globalMax = sortedEnv[^1];
			float globalMedian = sortedEnv[n / 2];
			float noiseFloor = sortedEnv[Math.Max(0, (int) (n * 0.02f))];

			if (globalMax <= 1e-7f)
            {
                return TimeSpan.Zero;
            }

            float eps = 1e-9f;

			int windowFrames = (int) (20.0 * sampleRate / downsampleFactor);
			windowFrames = Math.Max(1, Math.Min(envLen, windowFrames));

			float startMax = 0f;
			float startMedian;
			{
				var tmp = new float[windowFrames];
				Array.Copy(env, 0, tmp, 0, windowFrames);
				Array.Sort(tmp);
				startMax = tmp[^1];
				startMedian = tmp[tmp.Length / 2];
			}

			float endMax = 0f;
			float endMedian;
			{
				var tmp = new float[windowFrames];
				Array.Copy(env, envLen - windowFrames, tmp, 0, windowFrames);
				Array.Sort(tmp);
				endMax = tmp[^1];
				endMedian = tmp[tmp.Length / 2];
			}

			float startMaxRatio = startMax / Math.Max(globalMax, eps);
			float endMaxRatio = endMax / Math.Max(globalMax, eps);
			float startMedRatio = globalMedian > eps ? (startMedian / globalMedian) : 0f;
			float endMedRatio = globalMedian > eps ? (endMedian / globalMedian) : 0f;

			float silenceFloorBase;
			if (threshold.HasValue)
			{
				silenceFloorBase = Math.Max(threshold.Value, 1e-7f);
			}
			else
			{
				float dynRange = globalMax / Math.Max(noiseFloor, eps);
				float dynDb = 20f * MathF.Log10(Math.Max(1.0001f, dynRange));

				float alpha;
				if (dynDb < 20f)
                {
                    alpha = 0.4f;
                }
                else if (dynDb < 40f)
                {
                    alpha = 0.25f;
                }
                else if (dynDb < 60f)
                {
                    alpha = 0.12f;
                }
                else
                {
                    alpha = 0.06f;
                }

                float upperRef = sortedEnv[Math.Max(0, (int) (n * 0.8f))];
				float target = noiseFloor + (upperRef - noiseFloor) * alpha;

				float minFloor = globalMax * 0.0001f;
				float maxFloor = globalMax * 0.05f;
				silenceFloorBase = Math.Clamp(target, minFloor, maxFloor);
			}

			float digitalSilenceFloor = Math.Max(globalMax * 0.00005f, silenceFloorBase * 0.3f);
			int minSilenceFrames = Math.Max(2, minDurSamples / downsampleFactor);

			if (findStart)
			{
				bool startLooksLikeContent = startMaxRatio >= 0.25f || startMedRatio >= 0.5f;

				float floor = startLooksLikeContent
					? digitalSilenceFloor
					: Math.Min(silenceFloorBase, globalMax * 0.03f);

				int i = 0;
				while (i < envLen && env[i] <= floor)
                {
                    i++;
                }

                if (i < minSilenceFrames)
                {
                    return TimeSpan.Zero;
                }

                int sampleIdx = Math.Min(totalFrames - 1, i * downsampleFactor);
				sampleIdx = Math.Max(0, sampleIdx - sampleRate / 200);

				double seconds = sampleIdx / (double) sampleRate;
				return TimeSpan.FromSeconds(seconds);
			}
			else
			{
				bool endLooksLikeContent = endMaxRatio >= 0.3f || endMedRatio >= 0.6f;

				float floor = endLooksLikeContent
					? digitalSilenceFloor
					: Math.Min(silenceFloorBase * 1.2f, globalMax * 0.04f);

				int i = envLen - 1;
				while (i >= 0 && env[i] <= floor)
                {
                    i--;
                }

                int trailingFrames = (envLen - 1) - i;
				if (trailingFrames < minSilenceFrames)
                {
                    return TimeSpan.Zero;
                }

                int trailingSamples = trailingFrames * downsampleFactor;
				trailingSamples = Math.Max(0, trailingSamples - sampleRate / 200);

				double seconds = trailingSamples / (double) sampleRate;
				return TimeSpan.FromSeconds(seconds);
			}
		}

		private static float CalculateRobustThreshold(float[] env, int sampleRate)
		{
			if (env == null || env.Length == 0)
            {
                return 1e-5f;
            }

            var sorted = env.OrderBy(x => x).ToArray();
			float q1 = sorted[Math.Max(0, sorted.Length / 4)];
			float median = sorted[sorted.Length / 2];
			float mean = env.Average();
			float max = sorted[^1];

			float thr = MathF.Max(q1 * 4f, median * 0.7f);
			thr = Math.Clamp(thr, 1e-6f, MathF.Max(1e-3f, max * 0.5f));
			LogCollection.Log($"Calculated silence threshold: {thr:F6} (q1:{q1:F6}, median:{median:F6}, mean:{mean:F6}, max:{max:F6})");
			return thr;
		}

		private static float[] MovingAverage(float[] data, int win)
		{
			if (win <= 1)
            {
                return data;
            }

            var outArr = new float[data.Length];
			int h = win / 2;
			for (int i = 0; i < data.Length; i++)
			{
				int s = Math.Max(0, i - h);
				int e = Math.Min(data.Length - 1, i + h);
				double sum = 0;
				for (int j = s; j <= e; j++)
                {
                    sum += data[j];
                }

                outArr[i] = (float) (sum / (e - s + 1));
			}
			return outArr;
		}

		private static (float[] mono, float[] envelope) CreateMonoAndEnvelope(float[] data, int channels, int sampleRate, int totalFrames, int startSample)
		{
			int analysisLen = Math.Max(0, totalFrames - startSample);
			var mono = new float[analysisLen];

			if (channels == 1)
			{
				Array.Copy(data, startSample, mono, 0, analysisLen);
			}
			else
			{
                Parallel.For(0, analysisLen, i =>
				{
					int baseIdx = (startSample + i) * channels;
					float sum = 0f;
					for (int c = 0; c < channels; c++)
                    {
                        sum += data[baseIdx + c];
                    }

                    mono[i] = sum / channels;
				});
			}

			var env = new float[analysisLen];
			for (int i = 0; i < analysisLen; i++)
            {
                env[i] = MathF.Abs(mono[i]);
            }

            int w = Math.Max(1, Math.Min(101, sampleRate / 200));
			env = FastMovingAverage(env, w);

            Parallel.For(0, env.Length, i => env[i] = MathF.Sqrt(env[i]));

			return (mono, env);
		}

		private static float[] FastMovingAverage(float[] data, int window)
		{
			int n = data.Length;
			if (n == 0 || window <= 1)
            {
                return data;
            }

            var outArr = new float[n];
			double sum = 0;
			int half = window / 2;
			int s = 0, e = -1;
			for (int i = 0; i < n; i++)
			{
				int newE = Math.Min(n - 1, i + half);
				while (e < newE)
				{
					e++;
					sum += data[e];
				}
				int newS = Math.Max(0, i - half);
				while (s < newS)
				{
					sum -= data[s];
					s++;
				}
				int len = e - s + 1;
				outArr[i] = (float) (sum / Math.Max(1, len));
			}
			return outArr;
		}

		private static float[] BuildOnsetEnvelope(float[] env, int sampleRate, int downsampleHz)
		{
			if (env == null || env.Length == 0)
            {
                return Array.Empty<float>();
            }

            int dsFactor = Math.Max(1, sampleRate / downsampleHz);
			int outLen = Math.Max(1, env.Length / dsFactor);
			var outEnv = new float[outLen];
			for (int i = 0; i < outLen; i++)
			{
				int s = i * dsFactor;
				int e = Math.Min(env.Length - 1, s + dsFactor - 1);
				float maxv = 0f;
				for (int j = s; j <= e; j++)
                {
                    if (env[j] > maxv)
                    {
                        maxv = env[j];
                    }
                }

                outEnv[i] = maxv;
			}
			float max = outEnv.Max();
			if (max > 0)
            {
                for (int i = 0; i < outEnv.Length; i++)
                {
                    outEnv[i] /= max;
                }
            }

            return outEnv;
		}

		public static async Task<bool[]> GenerateBeatGridAsync(AudioObj audio, bool set = true, int granularity = 4)
		{
			if (audio == null || audio.Data == null || audio.Data.Length == 0 || audio.SampleRate <= 0)
            {
                return Array.Empty<bool>();
            }

            return await Task.Run(() =>
			{
				try
				{
					var grid = GenerateBeatGridInternal(audio, granularity);
					if (set)
                    {
                        audio.BeatGrid = grid;
                    }

                    return grid;
				}
				catch (Exception ex)
				{
					LogCollection.Log($"Error in beat grid detection: {ex.Message}");
					return new bool[audio.Data?.Length ?? 0];
				}
			});
		}

		private static bool[] GenerateBeatGridInternal(AudioObj audio, int granularity)
		{
			int sampleRate = audio.SampleRate;
			int channels = Math.Max(1, audio.Channels);
			float[] data = audio.Data;
			int totalFrames = data.Length / channels;

			TimeSpan startSilence = ComputeSilenceDuration(audio, true, null, 50);
			int startSample = (int) Math.Round(startSilence.TotalSeconds * sampleRate);
			startSample = Math.Clamp(startSample, 0, Math.Max(0, totalFrames - 1));

			var pre = CreateMonoAndEnvelope(data, channels, sampleRate, totalFrames, startSample);
			float[] mono = pre.mono;
			float[] envelope = pre.envelope;

			if (mono.Length < sampleRate / 2)
            {
                return new bool[totalFrames];
            }

            int downsampleHz = 200;
			float[] envDs = BuildOnsetEnvelope(envelope, sampleRate, downsampleHz);
			if (envDs == null || envDs.Length < 32)
            {
                return new bool[totalFrames];
            }

            var tempoPhase = EstimateTempoAndPhase(envDs, sampleRate, downsampleHz);
			int intervalFrames = tempoPhase.intervalFrames;
			int phaseFrames = tempoPhase.phaseOffsetFrames;

			if (intervalFrames <= 0)
            {
                return new bool[totalFrames];
            }

            granularity = Math.Clamp(granularity, 1, 16);
			intervalFrames = Math.Max(1, (int) Math.Round((double) intervalFrames / granularity) * granularity);

			var beatGrid = new bool[totalFrames];
			BuildBeatGridFromTempoPhase(beatGrid, intervalFrames, startSample + phaseFrames);

			ApplyGlobalAttackOffset(beatGrid, mono, envelope, startSample, sampleRate, intervalFrames);

			for (int i = 0; i < Math.Min(startSample, beatGrid.Length); i++)
            {
                beatGrid[i] = false;
            }

            int minDist = Math.Max(1, intervalFrames / 4);
			EnforceMinDistance(beatGrid, minDist);

			int beatCount = beatGrid.Count(b => b);
			double bpm = 60.0 * sampleRate / intervalFrames;
			LogCollection.Log($"Beat grid detection finished. Beats: {beatCount}, intervalFrames={intervalFrames}, bpm≈{bpm:F1}");

			return beatGrid;
		}
		

		private static (int intervalFrames, int phaseOffsetFrames) EstimateTempoAndPhase(float[] envDs, int sampleRate, int downsampleHz)
		{
			if (envDs == null || envDs.Length < 32)
            {
                return (0, 0);
            }

            int n = envDs.Length;
			int minBpm = 70;
			int maxBpm = 180;

			int minLag = (int) Math.Round(downsampleHz * 60.0 / maxBpm);
			int maxLag = (int) Math.Round(downsampleHz * 60.0 / minBpm);
			minLag = Math.Max(2, minLag);
			maxLag = Math.Min(n / 2, Math.Max(minLag + 1, maxLag));

			double bestScore = double.MinValue;
			int bestLag = 0;
			int bestPhase = 0;

			for (int lag = minLag; lag <= maxLag; lag++)
			{
				int L = lag;
				var phaseSum = new double[L];

				for (int i = 0; i < n; i++)
				{
					int p = i % L;
					phaseSum[p] += envDs[i];
				}

				double localBest = double.MinValue;
				int localPhase = 0;
				for (int p = 0; p < L; p++)
				{
					double s = phaseSum[p];
					if (s > localBest)
					{
						localBest = s;
						localPhase = p;
					}
				}

				double beats = (double) n / L;
				double avg = localBest / Math.Max(1.0, beats);
				double bpm = 60.0 * downsampleHz / L;
				double weight = Math.Exp(-Math.Pow(bpm - 120.0, 2.0) / (2.0 * 40.0 * 40.0));
				double score = avg * weight;

				if (score > bestScore)
				{
					bestScore = score;
					bestLag = L;
					bestPhase = localPhase;
				}
			}

			if (bestLag <= 0)
            {
                return (0, 0);
            }

            int dsFactor = Math.Max(1, sampleRate / downsampleHz);
			int intervalFrames = bestLag * dsFactor;
			int phaseFrames = bestPhase * dsFactor;

			return (intervalFrames, phaseFrames);
		}

		private static void BuildBeatGridFromTempoPhase(bool[] beatGrid, int intervalFrames, int firstBeatFrame)
		{
			if (beatGrid == null || beatGrid.Length == 0 || intervalFrames <= 0)
            {
                return;
            }

            int n = beatGrid.Length;

			int start = firstBeatFrame;
			while (start - intervalFrames >= 0)
            {
                start -= intervalFrames;
            }

            if (start < 0)
            {
                start += intervalFrames;
            }

            for (int pos = start; pos < n; pos += intervalFrames)
            {
                beatGrid[pos] = true;
            }
        }

		private static void SnapBeatsToEnvelopePeaks(bool[] beatGrid, float[] envelope, int startSample, int maxShiftFrames)
		{
			if (beatGrid == null || envelope == null)
            {
                return;
            }

            int n = beatGrid.Length;
			int envLen = envelope.Length;
			if (n == 0 || envLen == 0)
            {
                return;
            }

            maxShiftFrames = Math.Max(1, maxShiftFrames);
			var newGrid = new bool[n];

			float maxEnv = envelope.Max();
			if (maxEnv <= 0f)
			{
				Array.Copy(beatGrid, newGrid, n);
				Array.Copy(newGrid, beatGrid, n);
				return;
			}

			float thr = maxEnv * 0.12f;

			for (int i = 0; i < n; i++)
			{
				if (!beatGrid[i])
                {
                    continue;
                }

                int center = i - startSample;
				if (center < 0 || center >= envLen)
				{
					if (i >= 0 && i < n)
                    {
                        newGrid[i] = true;
                    }

                    continue;
				}

				int radius = Math.Min(maxShiftFrames, envLen - 1);
				int s = Math.Max(0, center - radius);
				int e = Math.Min(envLen - 1, center + radius);

				int bestIdx = center;
				float bestVal = envelope[center];

				for (int j = s; j <= e; j++)
				{
					float v = envelope[j];
					if (v > bestVal)
					{
						bestVal = v;
						bestIdx = j;
					}
				}

				if (bestVal < thr)
				{
					if (i >= 0 && i < n)
                    {
                        newGrid[i] = true;
                    }

                    continue;
				}

				int newFrame = startSample + bestIdx;
				if (newFrame < 0 || newFrame >= n)
                {
                    newFrame = Math.Clamp(i, 0, n - 1);
                }

                newGrid[newFrame] = true;
			}

			Array.Copy(newGrid, beatGrid, n);
		}

		private static void EnforceMinDistance(bool[] beatGrid, int minDistance)
		{
			if (beatGrid == null || beatGrid.Length == 0 || minDistance <= 0)
            {
                return;
            }

            int last = -minDistance - 1;
			for (int i = 0; i < beatGrid.Length; i++)
			{
				if (!beatGrid[i])
                {
                    continue;
                }

                if (i - last < minDistance)
				{
					beatGrid[i] = false;
				}
				else
				{
					last = i;
				}
			}
		}

		private static void ApplyGlobalAttackOffset(bool[] beatGrid, float[] mono, float[] envelope, int startSample, int sampleRate, int intervalFrames)
		{
			if (beatGrid == null || mono == null || envelope == null)
            {
                return;
            }

            int n = beatGrid.Length;
			if (n == 0 || mono.Length == 0 || envelope.Length == 0)
            {
                return;
            }

            var beats = new List<int>();
			for (int i = 0; i < n; i++)
			{
				if (beatGrid[i])
				{
					beats.Add(i);
					if (beats.Count >= 64)
                    {
                        break;
                    }
                }
			}

			if (beats.Count < 3)
            {
                return;
            }

            var offsets = new List<int>();
			foreach (int b in beats)
			{
				int onsetFrame = FindAttackStartForBeat(b, mono, envelope, startSample, sampleRate, intervalFrames);
				if (onsetFrame >= 0)
                {
                    offsets.Add(onsetFrame - b);
                }
            }

			if (offsets.Count < 2)
            {
                return;
            }

            offsets.Sort();
			int medianOffset = offsets[offsets.Count / 2];

			if (medianOffset == 0)
            {
                return;
            }

            var newGrid = new bool[n];
			for (int i = 0; i < n; i++)
			{
				if (!beatGrid[i])
                {
                    continue;
                }

                int ni = i + medianOffset;
				if (ni >= 0 && ni < n)
                {
                    newGrid[ni] = true;
                }
            }

			Array.Copy(newGrid, beatGrid, n);
		}

		private static int FindAttackStartForBeat(int beatFrame, float[] mono, float[] envelope, int startSample, int sampleRate, int intervalFrames)
		{
			int envLen = envelope.Length;
			int localIdx = beatFrame - startSample;
			if (localIdx <= 0 || localIdx >= envLen)
            {
                return -1;
            }

            int backMax = Math.Max(sampleRate / 4, intervalFrames / 2);
			int fwdMax = Math.Max(sampleRate / 8, intervalFrames / 4);

			int back = Math.Min(localIdx, backMax);
			int fwd = Math.Min(envLen - 1 - localIdx, fwdMax);

			int s = localIdx - back;
			int e = localIdx + fwd;

			int peakIdx = s;
			float peakVal = envelope[s];
			for (int i = s + 1; i <= e; i++)
			{
				float v = envelope[i];
				if (v > peakVal)
				{
					peakVal = v;
					peakIdx = i;
				}
			}

			if (peakVal <= 0f)
            {
                return beatFrame;
            }

            float thr = peakVal * 0.2f;
			int attackIdx = peakIdx;
			for (int i = peakIdx; i >= s; i--)
			{
				if (envelope[i] <= thr)
				{
					attackIdx = i;
					break;
				}
			}

			int zcIndex = FindNearestZeroCrossing(mono, attackIdx, sampleRate / 200);
			int onsetFrame = startSample + zcIndex;
			return onsetFrame;
		}

		private static int FindNearestZeroCrossing(float[] mono, int index, int maxSearch)
		{
			int n = mono.Length;
			if (n < 2)
            {
                return index;
            }

            index = Math.Clamp(index, 1, n - 1);
			maxSearch = Math.Max(1, maxSearch);

			for (int d = 0; d <= maxSearch; d++)
			{
				int left = index - d;
				if (left > 0)
				{
					float a = mono[left - 1];
					float b = mono[left];
					if (a == 0f)
                    {
                        return left - 1;
                    }

                    if (Math.Sign(a) != Math.Sign(b))
                    {
                        return left;
                    }
                }

				int right = index + d;
				if (right < n)
				{
					float a = mono[right - 1];
					float b = mono[right];
					if (a == 0f)
                    {
                        return right - 1;
                    }

                    if (Math.Sign(a) != Math.Sign(b))
                    {
                        return right;
                    }
                }
			}

			return index;
		}



	}
}
