using ModularAudience.Audio.Processors_V1;
using NAudio.Wave;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ModularAudience.Audio
{
    public partial class AudioObj : IDisposable
    {
        public CustomTags CustomTags { get; } = new();

        public void Dispose()
        {
            AudioPlaybackService.Unregister(this);
            this.Playing = false;
            this.Paused = false;
            this.Data = [];

            try { this.playback.Stop(); } catch { }
            this.playbackLoopApplied = false;
            this.playbackLoopStartBytes = 0;
            this.playbackLoopEndBytes = 0;
            GC.SuppressFinalize(this);
        }

        public bool LoadAudioFile()
        {
            if (string.IsNullOrWhiteSpace(this.FilePath))
            {
                return false;
            }

            this.Name = Path.GetFileNameWithoutExtension(this.FilePath);
            this.OriginalName = this.Name;
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                using var reader = new AudioFileReader(this.FilePath);
                this.SampleRate = reader.WaveFormat.SampleRate;
                this.Channels = reader.WaveFormat.Channels;
                this.BitDepth = reader.WaveFormat.BitsPerSample;

                long numSamples = reader.Length > 0 && reader.WaveFormat.BitsPerSample > 0
                    ? reader.Length / (reader.WaveFormat.BitsPerSample / 8)
                    : 0;

                if (numSamples > 0)
                {
                    try
                    {
                        float[] tmp = new float[numSamples];
                        int read = reader.Read(tmp, 0, (int) numSamples);
                        if (read != numSamples)
                        {
                            float[] resized = new float[read];
                            Array.Copy(tmp, resized, read);
                            this.Data = resized;
                        }
                        else
                        {
                            this.Data = tmp;
                        }
                    }
                    catch
                    {
                        // Fallback: stream read (fixed below to use proper block size)
                        this.Data = ReadAllSamplesStreaming(reader).ToArray();
                    }
                }
                else
                {
                    this.Data = ReadAllSamplesStreaming(reader).ToArray();
                }

                this.Length = this.Data.Length;
                this.Duration = reader.TotalTime;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading audio file: {ex.Message}");
                this.Dispose();
                return false;
            }

            this["Import"] = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            this.ReadBpmTag();
            this["ReadBpmTag"] = sw.Elapsed.TotalMilliseconds;

            return true;
        }


        public float ReadBpmTag(string tag = "TBPM", bool set = true)
        {
            float bpm = 0.0f;
            float roughBpm = 0.0f;

            try
            {
                if (!string.IsNullOrEmpty(this.FilePath) && File.Exists(this.FilePath))
                {
                    using var file = TagLib.File.Create(this.FilePath);
                    if (file.Tag.BeatsPerMinute > 0)
                    {
                        roughBpm = (float) file.Tag.BeatsPerMinute;
                    }

                    if (file.TagTypes.HasFlag(TagLib.TagTypes.Id3v2))
                    {
                        var id3v2Tag = (TagLib.Id3v2.Tag) file.GetTag(TagLib.TagTypes.Id3v2);
                        var tagTextFrame = TagLib.Id3v2.TextInformationFrame.Get(id3v2Tag, tag, false);

                        if (tagTextFrame != null && tagTextFrame.Text.Any())
                        {
                            string bpmString = tagTextFrame.Text.FirstOrDefault() ?? "0,0";
                            bpmString = bpmString.Replace(',', '.');
                            if (float.TryParse(bpmString, NumberStyles.Any, CultureInfo.InvariantCulture, out float parsedBpm))
                            {
                                bpm = parsedBpm;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Fehler beim Lesen des Tags {tag.ToUpperInvariant()}: {ex.Message} ({ex.InnerException?.Message ?? " - "})");
            }

            if (bpm > 0.0f && (bpm < 30.0f || bpm > 360.0f))
            {
                bpm = 0.0f;
            }

            if (bpm <= 0.0f && roughBpm > 0.0f)
            {
                if (roughBpm < 30.0f || roughBpm > 360.0f)
                {
                    roughBpm = 0.0f;
                }

                bpm = roughBpm;
            }

            if (set)
            {
                this.Bpm = bpm;
                if (this.Bpm <= 10)
                {
                    this.ReadBpmTagLegacy();
                }
            }

            return bpm;
        }

        public float ReadBpmTagLegacy(string tag = "BPM")
        {
            float bpm = 0.0f;

            try
            {
                if (!string.IsNullOrEmpty(this.FilePath) && File.Exists(this.FilePath))
                {
                    using var file = TagLib.File.Create(this.FilePath);
                    if (file.Tag.BeatsPerMinute > 0)
                    {
                        bpm = (float) file.Tag.BeatsPerMinute;
                    }
                    else if (file.TagTypes.HasFlag(TagLib.TagTypes.Id3v2))
                    {
                        var id3v2Tag = (TagLib.Id3v2.Tag) file.GetTag(TagLib.TagTypes.Id3v2);
                        var bpmFrame = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2Tag, tag, false);
                        if (bpmFrame != null && float.TryParse(bpmFrame.Text.FirstOrDefault(), out float parsedBpm))
                        {
                            bpm = parsedBpm;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Fehler beim Lesen der BPM: {ex.Message}");
            }

            this.Bpm = bpm > 0 ? bpm / 100.0f : 0.0f;
            return this.Bpm;
        }


        private static IEnumerable<float> ReadAllSamplesStreaming(AudioFileReader reader)
        {
            // FIX: Previously only 'channels' samples were read per iteration causing thousands of tiny reads.
            // Use a per-channel block of 1 second for efficient streaming.
            const int blockSeconds = 1;
            int channels = Math.Max(1, reader.WaveFormat.Channels);
            int samplesPerChannelPerBlock = reader.WaveFormat.SampleRate * blockSeconds;
            int blockSize = samplesPerChannelPerBlock * channels; // total interleaved samples
            float[] buffer = new float[blockSize];
            int read;
            while ((read = reader.Read(buffer, 0, blockSize)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    yield return buffer[i];
                }
            }
        }


        public static float? ReadFileBpmTag(string filePath, string tag = "TBPM")
        {
            if (!File.Exists(filePath) || Path.GetExtension(filePath) is not (".mp3" or ".wav" or ".flac" or ".m4a"))
            {
                return null;
            }

            float bpm = 0.0f;
            float roughBpm = 0.0f;

            try
            {
                using var file = TagLib.File.Create(filePath);
                if (file.Tag.BeatsPerMinute > 0)
                {
                    roughBpm = (float) file.Tag.BeatsPerMinute;
                }

                if (file.TagTypes.HasFlag(TagLib.TagTypes.Id3v2))
                {
                    var id3v2Tag = (TagLib.Id3v2.Tag) file.GetTag(TagLib.TagTypes.Id3v2);
                    var tagTextFrame = TagLib.Id3v2.TextInformationFrame.Get(id3v2Tag, tag, false);

                    if (tagTextFrame != null && tagTextFrame.Text.Any())
                    {
                        string bpmString = tagTextFrame.Text.FirstOrDefault() ?? "0,0";
                        bpmString = bpmString.Replace(',', '.');
                        if (float.TryParse(bpmString, NumberStyles.Any, CultureInfo.InvariantCulture, out float parsedBpm))
                        {
                            bpm = parsedBpm;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Fehler beim Lesen des Tags {tag.ToUpperInvariant()}: {ex.Message} ({ex.InnerException?.Message ?? " - "})");
            }

            if (bpm > 0.0f && (bpm < 30.0f || bpm > 360.0f))
            {
                bpm = 0.0f;
            }

            if (bpm <= 0.0f && roughBpm > 0.0f)
            {
                if (roughBpm < 30.0f || roughBpm > 360.0f)
                {
                    roughBpm = 0.0f;
                }

                bpm = roughBpm;
            }

            if (bpm <= 10)
            {
                bpm = ReadFileBpmTagLegacy(filePath);
            }

            return bpm;
        }

        public static float ReadFileBpmTagLegacy(string filePath, string tag = "BPM")
        {
            float bpm = 0.0f;
            try
            {
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    using var file = TagLib.File.Create(filePath);
                    if (file.Tag.BeatsPerMinute > 0)
                    {
                        bpm = (float) file.Tag.BeatsPerMinute;
                    }
                    else if (file.TagTypes.HasFlag(TagLib.TagTypes.Id3v2))
                    {
                        var id3v2Tag = (TagLib.Id3v2.Tag) file.GetTag(TagLib.TagTypes.Id3v2);
                        var bpmFrame = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2Tag, "BPM", false);
                        if (bpmFrame != null && float.TryParse(bpmFrame.Text.FirstOrDefault(), out float parsedBpm))
                        {
                            bpm = parsedBpm;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Fehler beim Lesen der BPM: {ex.Message}");
            }
            return bpm > 0 ? bpm / 100.0f : 0.0f;
        }


        public static Dictionary<string, float> ReadFilesBpmTags(IEnumerable<string> filePaths, string tag = "TBPM", int? maxWorkers = null, bool retryWithScanBpm = true, float filterMinBpm = 10f)
        {
            // Evaluate max parallel workers (half of the available processors if null, safely clamped to 1..ProcessorCount)
            maxWorkers = Math.Clamp(maxWorkers ?? Math.Max(1, Environment.ProcessorCount / 2), 1, Environment.ProcessorCount);

            // Verify & filter file paths; return [] if none are valid
            var validFiles = (filePaths?.Where(fp => !string.IsNullOrWhiteSpace(fp)
                && File.Exists(fp)
                && Path.GetExtension(fp) is (".mp3" or ".flac" or ".wav" or ".m4a")) ?? []).ToList();

            if (validFiles.Count == 0)
            {
                Debug.WriteLine("No valid audio files found for BPM tag reading.");
                return [];
            }

            // Thread-safe result dictionary
            var results = new ConcurrentDictionary<string, float>();

            Parallel.ForEach(validFiles, new ParallelOptions
            {
                MaxDegreeOfParallelism = maxWorkers.Value
            }, filePath =>
            {
                try
                {
                    float? bpm = ReadFileBpmTag(filePath, tag);
                    if (bpm > filterMinBpm)
                    {
                        results[filePath] = bpm ?? 0.0f;
                    }
                    else if (retryWithScanBpm)
                    {
                        bpm = BeatScanner.ScanFileBpm(filePath, 65536, 32);
                        if (bpm > filterMinBpm)
                        {
                            results[filePath] = bpm ?? 0.0f;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading BPM tags for '{filePath}': {ex.Message}");
                    results[filePath] = 0.0f;
                }
            });

            return results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }


    }
}
