using System.Diagnostics;
using System.IO;
using ModularAudience.Audio;
using NAudio.Wave;

namespace ModularAudience.Audio
{
    public class AudioExporter
    {
        // Fields
        public string ExportDirectory { get; set; }

        public static readonly Dictionary<string, int[]> AvailableExportFormats = new()
        {
            { ".wav", new[] { 8, 16, 24 } },
            { ".mp3", new[] { 24, 64, 96, 128, 192, 256, 320 } }
        };

        // Pfadbasierte Locks zur Vermeidung paralleler Zugriffe auf dieselbe Datei
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.SemaphoreSlim> _pathLocks = new(StringComparer.OrdinalIgnoreCase);

        private static async Task<IDisposable> AcquirePathLockAsync(string path)
        {
            var sem = _pathLocks.GetOrAdd(Path.GetFullPath(path), _ => new System.Threading.SemaphoreSlim(1, 1));
            await sem.WaitAsync().ConfigureAwait(false);
            return new Releaser(sem);
        }

        private sealed class Releaser : IDisposable
        {
            private readonly System.Threading.SemaphoreSlim _sem;

            public Releaser(System.Threading.SemaphoreSlim sem)
            {
                this._sem = sem;
            }

            public void Dispose()
            {
                this._sem.Release();
                // Kein Entfernen aus _pathLocks, um Race Conditions zu vermeiden
            }
        }

        // Ctor
        public AudioExporter(string? exportDirectory = null)
        {
            this.ExportDirectory = string.IsNullOrWhiteSpace(exportDirectory) ? AudioStoragePaths.ExportDirectory : exportDirectory;

            if (!Directory.Exists(this.ExportDirectory))
            {
                Directory.CreateDirectory(this.ExportDirectory);
            }
        }

        public static bool IsMp3Format(string? format)
        {
            return !string.IsNullOrWhiteSpace(format) && format.Contains('3');
        }

        public async Task<string?> ExportMp3Async(AudioObj audio, int bitrate = 192, int maxWorkers = 4, string? outDir = null, bool writeBpmTag = true)
        {
            maxWorkers = Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);

            // Verify audio
            if (audio.Data.LongLength <= 0 || audio.SampleRate <= 0 || audio.BitDepth <= 0 || audio.Channels <= 0)
            {
                return null;
            }

            if (string.IsNullOrEmpty(outDir))
            {
                outDir = this.ExportDirectory;
            }

            string targetPath;
            string? targetDirectory;
            bool hasMp3FileName = IsMp3Format(Path.GetExtension(outDir)) || IsMp3Format(Path.GetFileName(outDir));

            if (hasMp3FileName)
            {
                targetPath = Path.ChangeExtension(outDir, ".mp3");
                targetDirectory = Path.GetDirectoryName(targetPath);
            }
            else
            {
                targetDirectory = outDir;
                string baseOutFile = Path.Combine(targetDirectory, $"{SanitizeName(audio.Name)} [{audio.Bpm:F1}] {bitrate}kBit.mp3");
                targetPath = baseOutFile;
            }

            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                return "Out directory does not exist: (null)";
            }

            try
            {
                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }
            }
            catch (Exception ex)
            {
                LogCollection.Log($"Failed to ensure export directory '{targetDirectory}': {ex.Message}");
                return null;
            }

            string outFile = targetPath;

            Stopwatch sw = Stopwatch.StartNew();

            // Snapshot pre-existing files with the same base name so post-write dedup
            // can reuse an identical export instead of keeping a numbered duplicate.
            DateTime preExistingSnapshotTime = DateTime.UtcNow;
            var preExistingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string dir = Path.GetDirectoryName(targetPath) ?? string.Empty;
                string baseName = Path.GetFileNameWithoutExtension(targetPath);
                string ext = Path.GetExtension(targetPath);
                if (Directory.Exists(dir))
                    foreach (string f in Directory.GetFiles(dir, baseName + "*"))
                        if (string.Equals(Path.GetExtension(f), ext, StringComparison.OrdinalIgnoreCase))
                            preExistingFiles.Add(f);
            }
            catch { }

            try
            {
                using (await AcquirePathLockAsync(outFile))
                {
                    using var target = AudioExportFile.CreateUnique(outFile);
                    outFile = target.FilePath;
                    await Task.Run(() =>
                    {
                        // Konvertiere float[] Samples zu short[] PCM-Daten
                        short[] pcm = new short[checked((int)audio.Data.Length)];

                        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = maxWorkers };

                        Parallel.For(0, checked((int)audio.Data.Length), parallelOptions, i =>
                        {
                            float sample = Math.Clamp(audio.Data[i], -1.0f, 1.0f);
                            pcm[i] = (short)(sample * short.MaxValue);
                        });

                        byte[] pcmBytes = new byte[checked((int)(pcm.Length * sizeof(short)))];
                        Buffer.BlockCopy(pcm, 0, pcmBytes, 0, pcmBytes.Length);

                        var waveFormat = new WaveFormat(audio.SampleRate, audio.Channels);
                        using var ms = new MemoryStream();
                        using var writer = new NAudio.Lame.LameMP3FileWriter(ms, waveFormat, bitrate);

                        writer.Write(pcmBytes, 0, pcmBytes.Length);
                        writer.Flush();
                        target.Stream.Write(ms.ToArray());
                    });

                    target.Complete();
                    sw.Stop();
                    audio["Mp3Export"] = sw.Elapsed.TotalMilliseconds;

                    if (writeBpmTag && audio.Bpm > 10)
                    {
                        try
                        {
                            var tagFile = TagLib.File.Create(outFile);
                            tagFile.Tag.BeatsPerMinute = (uint)Math.Round(audio.Bpm);
                            tagFile.Save();
                        }
                        catch (Exception ex)
                        {
                            LogCollection.Log($"Failed to write BPM tag: {ex.Message}");
                        }
                    }

                    // Custom Tags aus dem TagEditor anwenden & get file size in MB:F2
                    ApplyCustomTagsToFile(audio, outFile);
                    float fileSizeMb = new FileInfo(outFile).Length / (1024f * 1024f);

                    LogCollection.Log($"Exported {audio.Name} to {outFile} ({fileSizeMb:F2} MB)");
                    outFile = DeduplicateExport(outFile, preExistingFiles, preExistingSnapshotTime);
                    audio.LastExportPath = outFile;
                }

                return outFile;
            }
            catch (Exception ex)
            {
                LogCollection.Log($"ExportMp3 failed: {ex.Message}");
                return null;
            }
        }

        public async Task<string?> ExportWavAsync(AudioObj audio, int bitDepth = 24, string? outDir = null, bool writeBpmTag = true, string? customFilePath = null, bool overwriteExisting = true)
        {
            if (audio.Data == null || audio.Data.Length == 0)
            {
                return "AudioObj data was null or empty";
            }

            string? targetDirectory = string.IsNullOrEmpty(outDir) ? null : outDir;
            string finalPath;

            if (!string.IsNullOrEmpty(customFilePath))
            {
                // Respect custom full file path, enforce .wav extension
                finalPath = Path.ChangeExtension(customFilePath, ".wav");
                targetDirectory = Path.GetDirectoryName(finalPath);
            }
            else
            {
                if (string.IsNullOrEmpty(targetDirectory))
                {
                    targetDirectory = this.ExportDirectory;
                }

                // Build sanitized base filename, then combine with directory and .wav extension
                string baseFileName = $"{SanitizeName(audio.Name.Replace("▶ ", string.Empty).Replace("|| ", string.Empty))}{(audio.Bpm > 10 ? (" [" + audio.Bpm.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "]") : string.Empty)}".Trim();
                string sanitizedFileName = SanitizeName(baseFileName) + ".wav";
                finalPath = Path.Combine(targetDirectory!, sanitizedFileName);
            }

            if (string.IsNullOrEmpty(targetDirectory))
            {
                return "Out directory does not exist: (null)";
            }

            try
            {
                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }
            }
            catch (Exception ex)
            {
                LogCollection.Log($"Failed to ensure export directory '{targetDirectory}': {ex.Message}");
                return null;
            }

            bool uniqueName = string.IsNullOrEmpty(customFilePath) || !overwriteExisting;

            DateTime preExistingSnapshotTime = DateTime.UtcNow;
            var preExistingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (uniqueName)
            {
                try
                {
                    string baseName = Path.GetFileNameWithoutExtension(finalPath);
                    string ext = Path.GetExtension(finalPath);
                    if (Directory.Exists(targetDirectory))
                        foreach (string f in Directory.GetFiles(targetDirectory, baseName + "*"))
                            if (string.Equals(Path.GetExtension(f), ext, StringComparison.OrdinalIgnoreCase))
                                preExistingFiles.Add(f);
                }
                catch { }
            }

            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                using (await AcquirePathLockAsync(finalPath))
                {
                    using var target = uniqueName ? AudioExportFile.CreateUnique(finalPath) : AudioExportFile.Overwrite(finalPath);
                    finalPath = target.FilePath;
                    await Task.Run(() =>
                    {
                        using var writer = new WaveFileWriter(target.Stream, new WaveFormat(audio.SampleRate, bitDepth, audio.Channels));

                        // Konvertierung und Schreiben
                        if (bitDepth == 32)
                        {
                            writer.WriteSamples(audio.Data, 0, checked((int)audio.Data.Length));
                        }
                        else if (bitDepth == 16)
                        {
                            short[] samples16Bit = new short[checked((int)audio.Data.Length)];
                            for (int i = 0; i < checked((int)audio.Data.Length); i++)
                            {
                                samples16Bit[i] = (short)(audio.Data[i] * short.MaxValue);
                            }
                            writer.WriteSamples(samples16Bit, 0, samples16Bit.Length);
                        }
                        else if (bitDepth == 8)
                        {
                            byte[] samples8Bit = new byte[checked((int)audio.Data.Length)];
                            for (int i = 0; i < checked((int)audio.Data.Length); i++)
                            {
                                // 8-Bit-PCM ist vorzeichenlos, 0 ist die Nulllinie
                                samples8Bit[i] = (byte)((audio.Data[i] + 1.0f) * 127.5f);
                            }
                            writer.Write(samples8Bit, 0, samples8Bit.Length);
                        }
                        else if (bitDepth == 24)
                        {
                            byte[] samples24Bit = new byte[checked((int)(audio.Data.Length * 3))];
                            int byteIndex = 0;
                            for (int i = 0; i < checked((int)audio.Data.Length); i++)
                            {
                                // Konvertiere Float zu 24-Bit-Integer und schreibe es als 3 Bytes
                                int value = (int)(audio.Data[i] * 8388607.0f); // 2^23 - 1
                                samples24Bit[byteIndex++] = (byte)(value);
                                samples24Bit[byteIndex++] = (byte)(value >> 8);
                                samples24Bit[byteIndex++] = (byte)(value >> 16);
                            }
                            writer.Write(samples24Bit, 0, samples24Bit.Length);
                        }
                        else
                        {
                            throw new ArgumentException("Ungültige Bit-Tiefe. Unterstützte Werte sind 8, 16, 24 und 32.");
                        }
                    });

                    target.Complete();
                    sw.Stop();
                    audio["WavExport"] = sw.Elapsed.TotalMilliseconds;

                    if (writeBpmTag && audio.Bpm > 10)
                    {
                        try
                        {
                            var tagFile = TagLib.File.Create(finalPath);
                            tagFile.Tag.BeatsPerMinute = (uint)Math.Round(audio.Bpm);
                            tagFile.Save();
                        }
                        catch (Exception ex)
                        {
                            LogCollection.Log($"Failed to write BPM tag: {ex.Message}");
                        }
                    }

                    ApplyCustomTagsToFile(audio, finalPath);
                    float fileSizeMb = new FileInfo(finalPath).Length / (1024f * 1024f);

                    LogCollection.Log($"Exported {audio.Name} to {finalPath} ({fileSizeMb:F2} MB)");
                    if (uniqueName) finalPath = DeduplicateExport(finalPath, preExistingFiles, preExistingSnapshotTime);
                    audio.LastExportPath = finalPath;
                }

                return finalPath;
            }
            catch (Exception ex)
            {
                LogCollection.Log($"Audio export failed to '{finalPath}': {ex.Message}");
                return null;
            }
        }



        private static void ApplyCustomTagsToFile(AudioObj audio, string filePath)
        {
            if (audio.CustomTags == null || !audio.CustomTags.HasData)
            {
                return;
            }

            try
            {
                var tagFile = TagLib.File.Create(filePath);
                var tag = tagFile.Tag;

                foreach (var kv in audio.CustomTags.Values)
                {
                    string id = kv.Key.ToUpperInvariant();
                    string value = kv.Value;

                    switch (id)
                    {
                        case "TT2": // Title
                            tag.Title = value;
                            break;

                        case "TT3": // Subtitle
                            tag.Subtitle = value;
                            break;

                        case "TT1": // Grouping
                            tag.Grouping = value;
                            break;

                        case "TAL": // Album
                            tag.Album = value;
                            break;

                        case "TP1": // Artist / Performers
                            tag.Performers = [value];
                            break;

                        case "TP2": // Album Artist / Band
                            tag.AlbumArtists = [value];
                            break;

                        case "TP3": // Conductor
                            tag.Conductor = value;
                            break;

                        case "TCM": // Composer
                            tag.Composers = [value];
                            break;

                        case "TCO": // Genre
                            tag.Genres = [value];
                            break;

                        case "COM": // Comment
                            tag.Comment = value;
                            break;

                        case "TYE": // Year
                            if (uint.TryParse(value, out var year))
                            {
                                tag.Year = year;
                            }
                            break;

                        case "TRK": // Track
                            if (uint.TryParse(value, out var track))
                            {
                                tag.Track = track;
                            }
                            break;

                        case "TPA": // Disc / Part of set
                            if (uint.TryParse(value, out var disc))
                            {
                                tag.Disc = disc;
                            }
                            break;

                        case "TBP": // BPM
                            if (uint.TryParse(value, out var bpm))
                            {
                                tag.BeatsPerMinute = bpm;
                            }
                            break;

                        case "ULT": // Lyrics
                            tag.Lyrics = value;
                            break;

                        default:
                            // Fallback: direktes Id3v2-Frame setzen
                            if (tagFile.TagTypes.HasFlag(TagLib.TagTypes.Id3v2))
                            {
                                var id3 = (TagLib.Id3v2.Tag)tagFile.GetTag(TagLib.TagTypes.Id3v2);
                                var frame = TagLib.Id3v2.TextInformationFrame.Get(id3, id, true);
                                frame.Text = [value];
                            }
                            break;
                    }
                }

                tagFile.Save();
            }
            catch (Exception ex)
            {
                LogCollection.Log($"Failed to write custom tags to {filePath}: {ex.Message}");
            }
        }



        public static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "untitled";
            }

            // Normalize Unicode
            name = name.Normalize(System.Text.NormalizationForm.FormC);

            // Invalid filename chars from the runtime
            char[] invalidChars = Path.GetInvalidFileNameChars();

            var sb = new System.Text.StringBuilder(name.Length);
            bool lastWasUnderscore = false;

            foreach (char ch in name)
            {
                // drop control chars and replace invalid chars with a single underscore
                if (char.IsControl(ch) || Array.IndexOf(invalidChars, ch) >= 0)
                {
                    if (!lastWasUnderscore)
                    {
                        sb.Append('_');
                        lastWasUnderscore = true;
                    }
                    continue;
                }

                // collapse runs of whitespace to a single space
                if (char.IsWhiteSpace(ch))
                {
                    if (sb.Length == 0 || char.IsWhiteSpace(sb[sb.Length - 1]))
                    {
                        continue;
                    }
                    sb.Append(' ');
                    lastWasUnderscore = false;
                    continue;
                }

                sb.Append(ch);
                lastWasUnderscore = false;
            }

            string result = sb.ToString().Trim();

            // collapse multiple underscores
            result = System.Text.RegularExpressions.Regex.Replace(result, "_{2,}", "_", System.Text.RegularExpressions.RegexOptions.None);

            // remove trailing dots/spaces (Windows forbids filenames ending with dot/space)
            result = result.TrimEnd('.', ' ');

            if (string.IsNullOrWhiteSpace(result))
            {
                result = "untitled";
            }

            // Avoid reserved device names (CON, PRN, AUX, NUL, COM1..COM9, LPT1..LPT9)
            string baseName = result;
            int dotIndex = result.IndexOf('.');
            if (dotIndex >= 0)
            {
                baseName = result.Substring(0, dotIndex);
            }

            var reserved = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CON","PRN","AUX","NUL"
            };
            for (int i = 1; i <= 9; i++)
            {
                reserved.Add("COM" + i);
                reserved.Add("LPT" + i);
            }

            if (reserved.Contains(baseName))
            {
                result = result + "_file";
            }

            // Limit filename length (leave headroom for paths); trim again if needed
            const int MaxLength = 200;
            if (result.Length > MaxLength)
            {
                result = result.Substring(0, MaxLength).TrimEnd('.', ' ');
            }

            return result;
        }

        /// <summary>
        /// Fast deduplication: if an existing file (snapshot taken before this export) has the
        /// same size and byte-identical content, reuse it instead of keeping a numbered duplicate.
        /// Quick path: size + first/last 64 bytes; exact path: full SHA-256.
        /// </summary>
        private static string DeduplicateExport(string newPath, HashSet<string> preExisting, DateTime snapshotTime)
        {
            try
            {
                if (string.IsNullOrEmpty(newPath) || !File.Exists(newPath)) return newPath;
                var newInfo = new FileInfo(newPath);
                byte[] newHash = ComputeFileHash(newPath);
                foreach (string existing in preExisting.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    if (string.Equals(existing, newPath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!File.Exists(existing)) continue;
                    // Only dedup against files that existed BEFORE this export batch.
                    if (new FileInfo(existing).LastWriteTimeUtc >= snapshotTime) continue;
                    var ei = new FileInfo(existing);
                    if (ei.Length != newInfo.Length) continue;
                    if (!FastPrefixSuffixEqual(existing, newPath, 64)) continue;
                    byte[] oldHash = ComputeFileHash(existing);
                    if (oldHash.SequenceEqual(newHash))
                    {
                        try { File.Delete(newPath); } catch { }
                        return existing;
                    }
                }
            }
            catch { }
            return newPath;
        }

        private static bool FastPrefixSuffixEqual(string a, string b, int bytes)
        {
            try
            {
                using var fa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var fb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (fa.Length != fb.Length) return false;
                byte[] ba = new byte[bytes], bb = new byte[bytes];
                int ra = fa.Read(ba, 0, bytes);
                int rb = fb.Read(bb, 0, bytes);
                if (ra != rb || !ba.AsSpan(0, ra).SequenceEqual(bb.AsSpan(0, rb))) return false;
                if (fa.Length <= bytes * 2) return true;
                fa.Seek(-bytes, SeekOrigin.End);
                fb.Seek(-bytes, SeekOrigin.End);
                ra = fa.Read(ba, 0, bytes);
                rb = fb.Read(bb, 0, bytes);
                return ra == rb && ba.AsSpan(0, ra).SequenceEqual(bb.AsSpan(0, rb));
            }
            catch { return false; }
        }

        private static byte[] ComputeFileHash(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return System.Security.Cryptography.SHA256.HashData(stream);
        }
    }
}
