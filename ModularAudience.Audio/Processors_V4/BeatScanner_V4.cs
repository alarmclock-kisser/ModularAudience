using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processors_V4
{
    /// <summary>
    /// Port of the original C++ Beat BPM Scanner algorithm.
    /// Provides async, thread-safe methods to calculate BPM from float audio samples.
    /// </summary>
    public static class BeatScanner_V4
    {
        /// <summary>
        /// Calculates the BPM of the provided audio samples asynchronously.
        /// </summary>
        /// <param name="samples">The raw float audio samples.</param>
        /// <param name="sampleRate">The sample rate of the audio (e.g., 44100).</param>
        /// <returns>The detected BPM as a float.</returns>
        public static async Task<float> ScanBpmAsync(float[] samples, int sampleRate = 44100)
        {
            if (samples == null || samples.Length == 0)
            {
                throw new ArgumentException("Samples cannot be null or empty.");
            }

            return await Task.Run(() =>
            {
                // 1. Peak Detection (simplified version of the internal logic)
                // The original code iterated over peak indices.
                var peakIndices = DetectPeaks(samples);

                if (peakIndices.Count < 2)
                {
                    return 0.0f;
                }

                // 2. Core Periodicity Logic (based on decompiled FUN_004090d0)
                double sumIntervals = 0;
                int intervalCount = 0;

                for (int i = 1; i < peakIndices.Count; i++)
                {
                    int interval = peakIndices[i] - peakIndices[i - 1];
                    sumIntervals += interval;
                    intervalCount++;
                }

                if (intervalCount == 0)
                {
                    return 0.0f;
                }

                float averageInterval = (float)(sumIntervals / intervalCount);

                // 3. Frequency Calculation
                // The logic used (AverageInterval / SampleRate) = Period (seconds)
                // Frequency (Hz) = 1 / Period
                // BPM = Frequency * 60
                
                float frequency = sampleRate / averageInterval;
                float bpm = frequency * 60.0f;

                return bpm;
            });
        }

        /// <summary>
        /// Detects peaks in the audio signal. 
        /// In a full port, this would mirror the exact internal signal thresholding logic.
        /// </summary>
        private static List<int> DetectPeaks(float[] samples)
        {
            var peaks = new List<int>();
            float threshold = 0.5f; // Simplified thresholding

            for (int i = 1; i < samples.Length - 1; i++)
            {
                if (Math.Abs(samples[i]) > threshold && 
                    Math.Abs(samples[i]) > Math.Abs(samples[i - 1]) && 
                    Math.Abs(samples[i]) > Math.Abs(samples[i + 1]))
                {
                    peaks.Add(i);
                }
            }
            return peaks;
        }
    }
}
