using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ModularAudience.Audio;
using ModularAudience.Audio.Synthesis;
using ModularAudience.Generators;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed class BreakbeatInstrumentSampleCache : IDisposable
{
    private sealed record Entry(
        float[] Data,
        int SampleRate,
        int Channels,
        int BitDepth,
        TimeSpan Duration,
        float Bpm,
        float Volume,
        string Name,
        long Used);

    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = [];
    private long bytes;
    private long clock;
    private bool disposed;

    public const long DefaultMaximumBytes = 256L * 1024 * 1024;
    public long MaximumBytes { get; }

    public BreakbeatInstrumentSampleCache(long maximumBytes = DefaultMaximumBytes)
    {
        this.MaximumBytes = Math.Max(0, maximumBytes);
    }

    public async Task<AudioObj> GetOrRenderAsync(InstrumentSynthSettings settings, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(settings);
        token.ThrowIfCancellationRequested();
        InstrumentSynthSettings snapshot = settings.Clone();
        snapshot.Validate();
        string key = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot)));

        Entry? cached = null;
        lock (this.gate)
        {
            if (!this.disposed && this.entries.TryGetValue(key, out Entry? entry))
            {
                cached = entry with { Used = ++this.clock };
                this.entries[key] = cached;
            }
        }

        if (cached is not null)
        {
            return CreateAudioView(cached);
        }

        token.ThrowIfCancellationRequested();
        Entry rendered = await Task.Run(() => RenderEntry(snapshot, token), token).ConfigureAwait(false);
        lock (this.gate)
        {
            if (!this.disposed)
            {
                if (this.entries.TryGetValue(key, out Entry? existing))
                {
                    rendered = existing with { Used = ++this.clock };
                    this.entries[key] = rendered;
                }
                else
                {
                    this.Store(key, rendered);
                    if (this.entries.TryGetValue(key, out existing))
                    {
                        rendered = existing;
                    }
                }
            }
        }

        token.ThrowIfCancellationRequested();
        return CreateAudioView(rendered);
    }

    private void Store(string key, Entry entry)
    {
        long size = entry.Data.LongLength * sizeof(float);
        if (size > this.MaximumBytes || this.MaximumBytes == 0)
        {
            return;
        }

        while (this.bytes + size > this.MaximumBytes && this.entries.Count > 0)
        {
            string oldestKey = this.entries.MinBy(pair => pair.Value.Used).Key;
            this.bytes -= this.entries[oldestKey].Data.LongLength * sizeof(float);
            this.entries.Remove(oldestKey);
        }

        Entry stored = entry with { Used = ++this.clock };
        this.entries.Add(key, stored);
        this.bytes += size;
    }

    private static Entry RenderEntry(InstrumentSynthSettings settings, CancellationToken token)
    {
        using AudioObj rendered = InstrumentSampleSynth.Render(settings, token);
        return new Entry(rendered.Data, rendered.SampleRate, rendered.Channels, rendered.BitDepth,
            rendered.Duration, rendered.Bpm, rendered.Volume, rendered.Name, 0);
    }

    private static AudioObj CreateAudioView(Entry entry) => new()
    {
        Data = entry.Data,
        SampleRate = entry.SampleRate,
        Channels = entry.Channels,
        BitDepth = entry.BitDepth,
        Length = entry.Data.Length,
        Duration = entry.Duration,
        Bpm = entry.Bpm,
        Volume = entry.Volume,
        Name = entry.Name
    };

    public void Dispose()
    {
        lock (this.gate)
        {
            this.disposed = true;
            this.entries.Clear();
            this.bytes = 0;
        }
    }
}

internal static class BreakbeatPatternInstrumentRenderer
{
    private const long MaximumPreparedInstrumentBytes = 512L * 1024 * 1024;

    public static async Task<AudioObj?> RenderAsync(
        IReadOnlyList<BreakbeatPatternNote> notes,
        IReadOnlyList<AudioObj> samples,
        IReadOnlyList<InstrumentSynthSettings?> instrumentSettingsByTrack,
        IReadOnlyList<BreakbeatTrackSettings> trackSettings,
        int bars,
        float bpm,
        int resolution,
        float swing,
        string? patternName,
        CancellationToken token,
        BreakbeatInstrumentSampleCache instrumentCache,
        PatternRenderCache? renderCache = null)
    {
        ArgumentNullException.ThrowIfNull(instrumentCache);
        token.ThrowIfCancellationRequested();

        List<AudioObj> renderSamples = [.. samples];
        List<BreakbeatTrackSettings> renderTrackSettings = [.. trackSettings];
        while (renderTrackSettings.Count < renderSamples.Count)
        {
            renderTrackSettings.Add(new BreakbeatTrackSettings());
        }

        List<BreakbeatPatternNote> renderNotes = new(notes.Count);
        List<AudioObj> temporarySamples = [];
        Dictionary<string, int> variantIndices = new(StringComparer.Ordinal);
        long preparedBytes = 0;

        try
        {
            foreach (BreakbeatPatternNote note in notes)
            {
                token.ThrowIfCancellationRequested();
                if (note.TrackIndex < 0
                    || note.TrackIndex >= samples.Count
                    || note.TrackIndex >= instrumentSettingsByTrack.Count
                    || instrumentSettingsByTrack[note.TrackIndex] is not InstrumentSynthSettings sourceSettings)
                {
                    renderNotes.Add(note);
                    continue;
                }

                BreakbeatTrackSettings track = note.TrackIndex < trackSettings.Count
                    ? trackSettings[note.TrackIndex]
                    : new BreakbeatTrackSettings();
                InstrumentSynthSettings pitchedSettings = CreateSettingsForNote(sourceSettings, note, track, bpm);
                string key = note.TrackIndex.ToString(CultureInfo.InvariantCulture) + ":"
                    + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(pitchedSettings)));

                if (!variantIndices.TryGetValue(key, out int variantIndex))
                {
                    long size = EstimateRenderBytes(pitchedSettings);
                    preparedBytes = checked(preparedBytes + size);
                    if (preparedBytes > MaximumPreparedInstrumentBytes)
                    {
                        throw new InvalidOperationException(
                            $"Rendering these instrument pitches requires {preparedBytes / (1024d * 1024d):0.#} MiB. "
                            + "Shorten the synthesized note, lower its sample rate, or use fewer distinct pitches.");
                    }

                    AudioObj rendered = await instrumentCache.GetOrRenderAsync(pitchedSettings, token).ConfigureAwait(false);
                    temporarySamples.Add(rendered);
                    variantIndex = renderSamples.Count;
                    variantIndices.Add(key, variantIndex);
                    renderSamples.Add(rendered);
                    renderTrackSettings.Add(track with { DefaultPitchSemitones = 0f });
                }

                renderNotes.Add(note with
                {
                    TrackIndex = variantIndex,
                    PitchSemitones = 0f,
                    TimeStretch = false,
                    Varispeed = false,
                    ManuallyResized = false,
                    Truncated = false,
                    HardResized = false,
                    OriginalDurationTicks = note.DurationTicks
                });
            }

            return await BreakbeatGenerator_V2.RenderPatternNotesAsync(
                renderNotes,
                renderSamples,
                bars,
                bpm,
                resolution,
                swing,
                patternName,
                token,
                renderTrackSettings,
                renderCache).ConfigureAwait(false);
        }
        finally
        {
            foreach (AudioObj sample in temporarySamples)
            {
                sample.Dispose();
            }
        }
    }

    internal static InstrumentSynthSettings CreateSettingsForNote(
        InstrumentSynthSettings source,
        BreakbeatPatternNote note,
        BreakbeatTrackSettings track,
        float bpm)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(track);
        if (!float.IsFinite(note.PitchSemitones))
        {
            throw new InvalidOperationException("An instrument note has an invalid pitch offset.");
        }

        if (note.DurationTicks <= 0)
        {
            throw new InvalidOperationException("An instrument note must have a positive duration.");
        }

        double trackPitch = float.IsFinite(track.DefaultPitchSemitones)
            ? Math.Clamp(track.DefaultPitchSemitones, -24f, 24f)
            : 0;
        double noteBeats = note.DurationTicks * 4d / BreakbeatGenerator_V2.PatternTicksPerBar;
        return ApplyPitch(source, trackPitch + note.PitchSemitones, bpm, noteBeats);
    }

    private static InstrumentSynthSettings ApplyPitch(
        InstrumentSynthSettings source,
        double semitones,
        float bpm,
        double noteBeats)
    {
        InstrumentSynthSettings pitched = source.Clone();
        pitched.Bpm = bpm;
        pitched.NoteBeats = noteBeats;
        pitched.DurationMultiplier = 1;
        if (pitched.Family == SynthFamily.DrumSet)
        {
            pitched.PitchSemitones += semitones;
        }
        else
        {
            double midiPitch = pitched.MidiNote + pitched.PitchSemitones + semitones;
            if (!double.IsFinite(midiPitch) || midiPitch is < 0 or > 127)
            {
                throw new InvalidOperationException("The pitched note is outside the supported MIDI range (0–127).");
            }

            int midiNote = (int)Math.Round(midiPitch);
            pitched.MidiNote = midiNote;
            pitched.PitchSemitones = midiPitch - midiNote;
        }

        pitched.Validate();
        return pitched;
    }

    private static long EstimateRenderBytes(InstrumentSynthSettings settings)
    {
        double frames = Math.Ceiling((settings.GateSeconds + settings.ReleaseSeconds) * settings.SampleRate);
        return checked((long)frames * settings.Channels * sizeof(float));
    }
}
