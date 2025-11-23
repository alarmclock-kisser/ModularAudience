using Microsoft.Win32;
using NAudience.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ModularAudience.Audio
{
    public class AudioCollection
    {
        // Fields
        public string WorkingDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "ModularAudience");
        public string ImportDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        public readonly BindingList<AudioObj> Audios = [];
        public readonly ConcurrentDictionary<Guid, CancellationToken> PlaybackCancellationTokens = [];

        public int BeatScanMinimumBpm { get; set; } = 70;
        public int BeatScanMaximumBpm { get; set; } = 210;

        public float Threshold { get; set; } = 0.001f;
        public int MinDurationMs { get; set; } = 50;
        public int MaxDurationMs { get; set; } = 650;
        public int SilenceWindowMs { get; set; } = 180;
        public double TruncateStartSeconds { get; set; } = 0.0;
        public double TruncateEndSeconds { get; set; } = 0.0;
        public bool KeepOriginal { get; set; } = false;

        // Objects
        public readonly AudioExporter Exporter;

        // Lambda
        public string ExportPath => Path.Combine(this.WorkingDirectory, "_Exports");
        public string RecordPath => Path.Combine(this.WorkingDirectory, "_Records");
        public int Count => this.Audios.Count;
        public IEnumerable<Guid> Ids => this.Audios.Select(a => a.Id);
        public IEnumerable<Guid> Playing => this.Audios.Where(a => a.Playing).Select(a => a.Id);

        // Indexer
        public AudioObj? this[Guid id] => this.Audios.FirstOrDefault(a => a.Id == id);
        public AudioObj? this[string name] => this.Audios.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public AudioCollection(string? workingDir = null, string? importDir = null, int wavRecordingBits = 24)
        {
            if (!string.IsNullOrEmpty(workingDir))
            {
                this.WorkingDirectory = workingDir;
            }
            if (!string.IsNullOrEmpty(importDir))
            {
                this.ImportDirectory = importDir;
            }
            Directory.CreateDirectory(this.WorkingDirectory);
            Directory.CreateDirectory(this.ExportPath);
            Directory.CreateDirectory(this.RecordPath);
            this.Exporter = new AudioExporter(this.ExportPath);
        }

        // Snapshot-Helfer: Wird vor einer destruktiven Änderung aufgerufen (z.B. Erase)
        // Legt aktuellen Zustand auf PreviousSteps und leert den Redo-Stack.
        public async Task<bool> PushSnapshotAsync(Guid id)
        {
            var audio = this[id];
            if (audio == null)
            {
                return false;
            }
            try
            {
                audio.NextSteps.Clear(); // Redo-Stack invalidieren bei neuer Änderung
                audio.PreviousSteps.Add(await audio.CloneAsync());
                return true;
            }
            catch (Exception ex)
            {
                LogCollection.Log(ex);
                return false;
            }
        }

        public async Task<AudioObj?> LoadAsync(string filePath)
        {
            var audio = await AudioObj.FromFileAsync(filePath);
            if (audio != null)
            {
                this.Audios.Add(audio);
            }
            return audio;
        }

        public async Task<IEnumerable<AudioObj?>> LoadManyAsync(IEnumerable<string> filePaths)
        {
            var tasks = filePaths.Select(AudioObj.FromFileAsync);
            var audios = await Task.WhenAll(tasks).ConfigureAwait(true);
            var loaded = audios.Where(a => a != null).ToList()!;
            foreach (var audio in loaded)
            {
                this.Audios.Add(audio!);
            }
            return loaded;
        }

        public async Task<IEnumerable<AudioObj?>> LoadDirectoryAsync(string directoryPath)
        {
            string[] exts = [".mp3", ".wav", ".flac"];
            var files = Directory.GetFiles(directoryPath)
                .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
            return await this.LoadManyAsync(files).ConfigureAwait(true);
        }

        public async Task<AudioObj?> RemoveAsync(Guid id, bool dispose = true)
        {
            var audio = this[id];
            if (audio != null)
            {
                this.Audios.Remove(audio);
                if (dispose)
                {
                    await Task.Run(audio.Dispose);
                }
            }
            return dispose ? null : audio;
        }

        public async Task LoadResources(string? resourcesPath = null)
        {
            resourcesPath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
            if (Directory.Exists(resourcesPath))
            {
                await this.LoadDirectoryAsync(resourcesPath).ConfigureAwait(true);
            }
        }

        public async Task ClearAsync()
        {
            foreach (var audio in this.Audios)
            {
                await Task.Run(audio.Dispose);
            }
            this.Audios.Clear();
        }

        // Einfache Wiederherstellung ohne Undo/Redo-Stack-Manipulation (legacy).
        public async Task RestoreFromAudioObjAsync(Guid id, int stepsBack = 1)
        {
            var audio = this[id];
            if (audio == null || stepsBack <= 0 || audio.PreviousSteps.Count < stepsBack)
            {
                return;
            }
            try
            {
                var snapshot = audio.PreviousSteps[^stepsBack];
                if (snapshot == null)
                {
                    return;
                }
                ApplyState(audio, snapshot);
            }
            catch (Exception ex)
            {
                LogCollection.Log(ex);
            }
            finally
            {
                await Task.CompletedTask;
            }
        }

        // Undo: aktueller Zustand -> NextSteps, letzter Snapshot -> aktiv, Snapshot aus PreviousSteps entfernen
        public async Task<bool> UndoAsync(Guid id)
        {
            var audio = this[id];
            if (audio == null || audio.PreviousSteps.Count == 0)
            {
                return false;
            }
            try
            {
                // Aktuellen Zustand für Redo sichern
                audio.NextSteps.Add(await audio.CloneAsync());

                // Letzten Snapshot holen und anwenden
                var snapshot = audio.PreviousSteps[^1];
                if (snapshot == null)
                {
                    return false;
                }
                ApplyState(audio, snapshot);

                // Aus Undo-Stack entfernen
                audio.PreviousSteps.RemoveAt(audio.PreviousSteps.Count - 1);
                return true;
            }
            catch (Exception ex)
            {
                LogCollection.Log(ex);
                return false;
            }
        }

        // Redo: aktuellen Zustand -> PreviousSteps, letzten Redo-Zustand anwenden und aus NextSteps entfernen
        public async Task<bool> RedoAsync(Guid id)
        {
            var audio = this[id];
            if (audio == null || audio.NextSteps.Count == 0)
            {
                return false;
            }
            try
            {
                // Aktuellen Zustand wieder als Undo-Snapshot sichern
                audio.PreviousSteps.Add(await audio.CloneAsync());

                // Redo-Ziel holen
                var redoState = audio.NextSteps[^1];
                audio.NextSteps.RemoveAt(audio.NextSteps.Count - 1);
                if (redoState == null)
                {
                    return false;
                }
                ApplyState(audio, redoState);
                return true;
            }
            catch (Exception ex)
            {
                LogCollection.Log(ex);
                return false;
            }
        }

        private static void ApplyState(AudioObj target, AudioObj source)
        {
            target.Data = (float[]) source.Data.Clone();
            target.SampleRate = source.SampleRate;
            target.Channels = source.Channels;
            target.BitDepth = source.BitDepth;
            target.Length = source.Length;
            target.Duration = source.Duration;
            target.Bpm = source.Bpm;
            target.Timing = source.Timing;
            target.Volume = source.Volume;
            target.SelectionStart = -1;
            target.SelectionEnd = -1;
            // Position bleibt unverändert
        }

        // Playback
        public async Task PlayManyAsync(IEnumerable<Guid> ids)
        {
            var tasks = ids.Select(id => this[id]?.PlayAsync(CancellationToken.None) ?? Task.CompletedTask);
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public async Task PlayAllAsync()
        {
            var tasks = this.Audios.Where(a => !a.Playing).Select(a => a.PlayAsync(CancellationToken.None));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public async Task StopManyAsync(IEnumerable<Guid> ids)
        {
            var tasks = ids.Select(id => this[id]?.StopAsync() ?? Task.CompletedTask);
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public async Task StopAllAsync()
        {
            var tasks = this.Audios.Select(async a =>
            {
                await a.StopAsync();
                a.StartingOffset = 0;
                a.ScrollOffset = 0;
            });
            await Task.WhenAll(tasks);
        }

        // Export
        public async Task<string?> ExportAsync(Guid id, string format = ".wav", int bits = 24)
        {
            format = AudioExporter.AvailableExportFormats.ContainsKey(format) ? format : ".wav";
            bits = AudioExporter.AvailableExportFormats[format].Contains(bits) ? bits : AudioExporter.AvailableExportFormats[format].Last();
            var audio = this[id];
            if (audio != null)
            {
                return format == ".mp3"
                    ? await this.Exporter.ExportMp3Async(audio, bits)
                    : await this.Exporter.ExportWavAsync(audio, bits);
            }
            return null;
        }

        public async Task<IEnumerable<string>> ExportManyAsync(IEnumerable<Guid> ids, string format = ".wav", int bits = 24)
        {
            format = AudioExporter.AvailableExportFormats.ContainsKey(format) ? format : ".wav";
            bits = AudioExporter.AvailableExportFormats[format].Contains(bits) ? bits : AudioExporter.AvailableExportFormats[format].Last();

            var tasks = ids.Select(id =>
            {
                var audio = this[id];
                if (audio != null)
                {
                    return format == ".mp3"
                        ? this.Exporter.ExportMp3Async(audio, bits)
                        : this.Exporter.ExportWavAsync(audio, bits);
                }
                return Task.FromResult<string?>(null);
            });

            var results = await Task.WhenAll(tasks);
            return results.Where(r => r != null).Select(r => r!);
        }

        public async Task<IEnumerable<string>> ExportAllAsync(string format = ".wav", int bits = 24)
        {
            return await this.ExportManyAsync(this.Audios.Select(a => a.Id), format, bits);
        }

        // Scanner
        public async Task<float?> ScanBpmAsync(Guid id, int windowSize = 16384, int lookingRange = 4, bool set = false)
        {
            var audio = this[id];
            if (audio == null)
            {
                return null;
            }
            float bpm = (float) await BeatScanner.ScanBpmAsync(audio, windowSize, lookingRange, this.BeatScanMinimumBpm, this.BeatScanMaximumBpm);
            if (set)
            {
                audio.Bpm = bpm;
            }

            return bpm;
        }

        // Helpers
        public static string? VerifyAudioFile(string filePath)
        {
            try
            {
                using var reader = new NAudio.Wave.AudioFileReader(filePath);
                return reader.FileName;
            }
            catch
            {
                return null;
            }
        }

        public static TimeSpan? GetTimeSpanFromFile(string filePath)
        {
            try
            {
                using var reader = new NAudio.Wave.AudioFileReader(filePath);
                return reader.TotalTime;
            }
            catch
            {
                return null;
            }
        }
    }
}
