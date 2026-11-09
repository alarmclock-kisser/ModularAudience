using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processing;

public sealed partial class PlaylistEngine
{
    // Queue identities, never temporary filenames. The collection retains ownership of RAM objects.
    private const string AudioReferencePrefix = "ram://";
    private readonly Dictionary<string, AudioReference> _audioReferences = new(StringComparer.OrdinalIgnoreCase);

    private sealed class AudioReference
    {
        public required Guid Id { get; init; }
        public required WeakReference<AudioObj> Source { get; init; }
        public required string Name { get; set; }
        public string FallbackPath { get; set; } = string.Empty;
        public bool CollectionAvailable { get; set; } = true;
        public bool RamAvailable { get; set; }
        public long Version { get; set; }
    }

    /// <summary>In-memory preprocessing. Null means use the borrowed original;
    /// a different AudioObj is owned by the playlist. RAM sources are never exported/reloaded here.</summary>
    public Func<AudioObj, CancellationToken, Task<AudioObj?>>? BeforeAudioTrackPlay { get; set; }

    /// <summary>Insert collection references as one Next Up batch. Prefer RAM; remember the latest real source/export path as fallback.</summary>
    public int EnqueueAudioNext(IEnumerable<AudioObj> audios)
    {
        ArgumentNullException.ThrowIfNull(audios);
        AudioObj[] selected = audios.DistinctBy(audio => audio.Id).ToArray();
        List<string> keys = [];
        lock (this._lock)
        {
            if (this._disposed) { return 0; }
            foreach (AudioObj audio in selected)
            {
                string key = AudioReferencePrefix + audio.Id.ToString("N");
                if (!this._audioReferences.TryGetValue(key, out AudioReference? reference))
                {
                    reference = new()
                    {
                        Id = audio.Id, Source = new(audio), Name = audio.Name,
                        FallbackPath = string.IsNullOrWhiteSpace(audio.LastExportPath) ? audio.FilePath : audio.LastExportPath,
                        RamAvailable = IsAvailableAudio(audio)
                    };
                    this._audioReferences.Add(key, reference);
                    audio.PropertyChanged += this.AudioReferenceSourceChanged;
                }
                reference.CollectionAvailable = true;
                if (IsAvailableAudio(audio) || File.Exists(reference.FallbackPath)) { keys.Add(key); }
            }
            if (keys.Count == 0) { return 0; }
            this.FilePaths.InsertRange(this.GetProtectedHeadCountLocked(), keys);
            this.InvalidateQueuedPreparedStartLocked();
        }
        TrackChanged?.Invoke();
        return keys.Count;
    }

    private void AudioReferenceSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AudioObj audio) { return; }
        lock (this._lock)
        {
            if (!this._audioReferences.TryGetValue(AudioReferencePrefix + audio.Id.ToString("N"), out var reference)) { return; }
            reference.Name = audio.Name;
            string? path = e.PropertyName switch
            {
                nameof(AudioObj.FilePath) => audio.FilePath,
                nameof(AudioObj.LastExportPath) => audio.LastExportPath,
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(path) &&
                (e.PropertyName == nameof(AudioObj.LastExportPath) ||
                 !string.Equals(path, reference.FallbackPath, StringComparison.OrdinalIgnoreCase)))
            {
                reference.FallbackPath = path;
                // A new export affects a disk fallback prepare, not an available RAM version.
                if (!reference.RamAvailable) { reference.Version++; }
            }
            else if (reference.RamAvailable && IsAvailableAudio(audio) && e.PropertyName is
                nameof(AudioObj.Data) or nameof(AudioObj.SampleRate) or nameof(AudioObj.Channels) or
                nameof(AudioObj.Bpm) or nameof(AudioObj.ScannedBpm))
            {
                reference.Version++;
            }
        }
    }

    /// <summary>Update live collection membership. Missing RAM falls back to its remembered path, or is removed if neither source exists.</summary>
    public void RemoveUnavailableAudioReferences(IEnumerable<Guid> availableIds) => this.PruneAudioReferences(availableIds.ToHashSet());

    private static bool IsAudioReference(string? key) => key?.StartsWith(AudioReferencePrefix, StringComparison.OrdinalIgnoreCase) == true;
    private static bool IsAvailableAudio(AudioObj audio) =>
        !audio.IsDisposed && audio.Data is { Length: > 0 } && audio.SampleRate > 0 && audio.Channels > 0;

    private bool TryGetReferencedAudioLocked(string key, out AudioObj? audio)
    {
        audio = null;
        return this._audioReferences.TryGetValue(key, out var reference) && reference.CollectionAvailable &&
            reference.Source.TryGetTarget(out audio) && IsAvailableAudio(audio);
    }

    private bool IsSourceAvailableLocked(string key) => !IsAudioReference(key) ||
        this.TryGetReferencedAudioLocked(key, out _) ||
        (this._audioReferences.TryGetValue(key, out var reference) && File.Exists(reference.FallbackPath));

    private bool IsPreparedSourceCurrentLocked(PreparedPlaylistTrack prepared) => !IsAudioReference(prepared.OriginalPath) ||
        (this._audioReferences.TryGetValue(prepared.OriginalPath, out var reference) &&
         prepared.AudioReferenceVersion == reference.Version &&
         prepared.UsesRamSource == this.TryGetReferencedAudioLocked(prepared.OriginalPath, out _) &&
         this.IsSourceAvailableLocked(prepared.OriginalPath));

    private bool IsTrackSourceAvailable(string key)
    {
        lock (this._lock)
        {
            return IsAudioReference(key) ? this.IsSourceAvailableLocked(key) : File.Exists(key);
        }
    }

    public string GetTrackDisplayName(string key)
    {
        lock (this._lock)
        {
            if (this._audioReferences.TryGetValue(key, out var reference))
            {
                return reference.Source.TryGetTarget(out var source) ? source.Name : reference.Name;
            }
        }
        return IsAudioReference(key) ? "Unavailable RAM track" : Path.GetFileNameWithoutExtension(key);
    }

    public string? GetTrackFilePath(string key)
    {
        lock (this._lock)
        {
            return IsAudioReference(key)
                ? (this._audioReferences.TryGetValue(key, out var reference) ? reference.FallbackPath : null)
                : key;
        }
    }

    public IReadOnlyList<AudioObj> GetQueuedAudioReferences()
    {
        lock (this._lock)
        {
            return this.FilePaths.Select(key => this.TryGetReferencedAudioLocked(key, out var audio) ? audio : null)
                .OfType<AudioObj>().DistinctBy(audio => audio.Id).ToArray();
        }
    }

    public (TimeSpan Duration, float Bpm, int Channels, int SampleRate, int BitDepth) GetTrackMetadata(string key)
    {
        string path = key;
        lock (this._lock)
        {
            if (this.TryGetReferencedAudioLocked(key, out AudioObj? audio))
            {
                return (audio!.Duration, audio.Bpm > 0 ? audio.Bpm : audio.ScannedBpm,
                    audio.Channels, audio.SampleRate, audio.BitDepth);
            }
            if (IsAudioReference(key))
            {
                path = this._audioReferences.TryGetValue(key, out var reference) ? reference.FallbackPath : string.Empty;
            }
        }
        return ReadMetadata(path);
    }

    private void PruneAudioReferences(HashSet<Guid>? availableIds = null)
    {
        List<(PreparedPlaylistTrack Prepared, bool WasActive)> removed = [];
        bool changed = false;
        lock (this._lock)
        {
            foreach ((string key, AudioReference reference) in this._audioReferences)
            {
                if (availableIds != null) { reference.CollectionAvailable = availableIds.Contains(reference.Id); }
                bool ramAvailable = this.TryGetReferencedAudioLocked(key, out _);
                bool lostRam = reference.RamAvailable && !ramAvailable;
                if (reference.RamAvailable != ramAvailable)
                {
                    reference.RamAvailable = ramAvailable;
                    reference.Version++;
                    changed = true;
                }
                bool unavailable = !ramAvailable && !File.Exists(reference.FallbackPath);
                if (unavailable)
                {
                    changed |= this.FilePaths.RemoveAll(path => QueuePathsEqual(path, key)) > 0;
                    if (QueuePathsEqual(this._previousPath, key)) { this._previousPath = null; }
                }
                bool current = QueuePathsEqual(this._queueCurrentPath, key) || QueuePathsEqual(this.OriginalCurrentPath, key);
                if ((lostRam || unavailable) && current)
                {
                    this._skipRequested = true;
                    this._playbackStateVersion++;
                    this.ClearCurrentTrackState();
                    // The old RAM run-loop must not consume the retained disk-fallback head.
                    this.ReserveQueueHeadLocked(null);
                    changed = true;
                }
                if (lostRam || unavailable)
                {
                    foreach (PreparedPlaylistTrack prepared in this._activePreparedTracks.Values
                        .Where(track => QueuePathsEqual(track.OriginalPath, key)).ToArray())
                    {
                        this._activePreparedTracks.Remove(prepared.Audio.Id);
                        removed.Add((prepared, true));
                        if (SameAudio(prepared.Audio, this._secondaryAudioObj)) { this.SetSecondaryTrack(null); }
                    }
                }
                string cacheKey = NormalizePathForKey(key);
                if (this._preparedByPath.TryGetValue(cacheKey, out var cached) && !this.IsPreparedSourceCurrentLocked(cached))
                {
                    this._preparedByPath.Remove(cacheKey);
                    if (!removed.Any(item => ReferenceEquals(item.Prepared, cached))) { removed.Add((cached, false)); }
                }
            }
            this.InvalidateQueuedPreparedStartLocked();
        }
        foreach (var item in removed)
        {
            if (item.WasActive) { _ = item.Prepared.Audio.StopAsync(); }
            DisposePreparedAudio(item.Prepared);
            this.DeleteTempFile(item.Prepared.TempPath);
        }
        if (changed) { TrackChanged?.Invoke(); }
    }

    private async Task<PreparedPlaylistTrack?> PrepareAudioReferenceAsync(string key, CancellationToken ct)
    {
        AudioObj? source;
        long version;
        string fallback;
        lock (this._lock)
        {
            if (!this._audioReferences.TryGetValue(key, out var reference)) { return null; }
            if (!this.TryGetReferencedAudioLocked(key, out source)) { source = null; }
            version = reference.Version;
            fallback = reference.FallbackPath;
        }
        if (source == null)
        {
            return File.Exists(fallback) ? await this.PrepareFileTrackCoreAsync(key, fallback, ct, version).ConfigureAwait(false) : null;
        }

        AudioObj? playback = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            playback = this.BeforeAudioTrackPlay == null ? source :
                await this.BeforeAudioTrackPlay(source, ct).ConfigureAwait(false) ?? source;
            ct.ThrowIfCancellationRequested();
            var prepared = new PreparedPlaylistTrack
            {
                Audio = playback, OriginalPath = key, PlayPath = key, OwnsAudio = !ReferenceEquals(playback, source),
                UsesRamSource = true, AudioReferenceVersion = version
            };
            // Only processed working copies may be normalized in-place, never the borrowed original.
            if (prepared.OwnsAudio) { PlaylistNormalizer.NormalizeToTarget(playback.Data); }
            lock (this._lock)
            {
                if (this._disposed || !this.IsPreparedSourceCurrentLocked(prepared) || !IsAvailableAudio(playback))
                {
                    DisposePreparedAudio(prepared);
                    return null;
                }
                this._preparedByPath[NormalizePathForKey(key)] = prepared;
            }
            return prepared;
        }
        catch (OperationCanceledException)
        {
            if (!ReferenceEquals(playback, source)) { playback?.Dispose(); }
            throw;
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(playback, source)) { playback?.Dispose(); }
            this.LogPlayback($"RAM preparation failed for '{key}': {ex.Message}");
            return null;
        }
    }

    private static void DisposePreparedAudio(PreparedPlaylistTrack prepared)
    {
        if (prepared.OwnsAudio) { try { prepared.Audio.Dispose(); } catch { } }
    }

    private bool OwnsPlaybackAudioLocked(AudioObj? audio) => audio != null &&
        !this._audioReferences.Values.Any(reference => reference.Source.TryGetTarget(out var source) && ReferenceEquals(source, audio)) &&
        !this._activePreparedTracks.Values.Concat(this._preparedByPath.Values)
            .Any(prepared => !prepared.OwnsAudio && ReferenceEquals(prepared.Audio, audio)) &&
        !(this._startingPreparedTrack is { OwnsAudio: false } starting && ReferenceEquals(starting.Audio, audio));

    private void DetachAudioReferences()
    {
        lock (this._lock)
        {
            foreach (AudioReference reference in this._audioReferences.Values)
            {
                if (reference.Source.TryGetTarget(out var source)) { source.PropertyChanged -= this.AudioReferenceSourceChanged; }
            }
            this._audioReferences.Clear();
        }
    }
}
