using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ModularAudience.Audio.Arrangement;

public enum ClipTempoMode { Original, Varispeed, TimeStretch }
public enum MixScrollMode { None, Follow, Page }

public sealed class MixVisualOptions
{
    public string Background { get; set; } = "#1D2026";
    public string TrackHeader { get; set; } = "#282C32";
    public string SelectedHeader { get; set; } = "#414B55";
    public string Grid { get; set; } = "#373D46";
    public string Clip { get; set; } = "#355B70";
    public string SelectedClip { get; set; } = "#4883A7";
    public string Waveform { get; set; } = "#9FD5E3";
    public string Text { get; set; } = "#FFFFFF";
    public string Playhead { get; set; } = "#FF4500";
    public string LoopRange { get; set; } = "#3CAA6E";
    public double Hue { get; set; }
    public bool HueCycle { get; set; }
    public double HueSpeed { get; set; } = 30;
    public bool ColorTracks { get; set; } = true;
    public bool ShowWaveforms { get; set; } = true;
    public bool ShowGrid { get; set; } = true;
    public double PixelsPerSecond { get; set; } = 90;
    public MixScrollMode ScrollMode { get; set; }
    public double HorizontalScrollSeconds { get; set; }
    public int FirstVisibleTrack { get; set; }
    public bool AutoTrackHeight { get; set; }
}

public sealed class MixSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Audio";
    public int SampleRate { get; set; } = 44100;
    public int Channels { get; set; } = 2;
    public float Bpm { get; set; }
    /// <summary>Immutable pre-tempo asset. Null denotes an independent original/edit baseline.</summary>
    public Guid? TempoOriginId { get; set; }
    /// <summary>Original seconds per second in this cached tempo rendering.</summary>
    public double TempoOriginScale { get; set; } = 1;
    public float TempoOriginBpm { get; set; }
    public MixTempoRecipe? TempoRecipe { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public float[] Data { get; set; } = [];
    public double Duration => Data.Length / (double)(Math.Max(1, Channels) * Math.Max(1, SampleRate));
}

public sealed class MixClip
{
    [System.ComponentModel.Browsable(false)]
    public Guid Id { get; set; } = Guid.NewGuid();
    [System.ComponentModel.Browsable(false)]
    public Guid SourceId { get; set; }
    public string Name { get; set; } = "Clip";
    public double Start { get; set; }
    public double SourceOffset { get; set; }
    public double SourceDuration { get; set; }
    public double Gain { get; set; } = 1;
    public double FadeIn { get; set; }
    public double FadeOut { get; set; }
    public ClipTempoMode TempoMode { get; set; }
    // Effect-region bounces already contain the original clip envelope and
    // overlap crossfades. Do not apply a second automatic envelope to them.
    [System.ComponentModel.Browsable(false)]
    public bool HasRenderedEnvelope { get; set; }
}

public sealed class MixLane
{
    [System.ComponentModel.Browsable(false)]
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Track";
    public bool Mute { get; set; }
    public bool Solo { get; set; }
    public double Gain { get; set; } = 1;
    public bool AutoCrossfade { get; set; }
    [System.ComponentModel.Browsable(false)] public List<MixClip> Clips { get; set; } = [];
}

/// <summary>Non-destructive timeline. Sources are immutable owned copies; positions are seconds.</summary>
public sealed class MixArrangement : IDisposable
{
    public string Name { get; set; } = "Mix";
    public double Bpm { get; set; } = 120;
    public int SnapDivision { get; set; } = 8;
    public bool Snap { get; set; }
    public int DisplayLaneHeight { get; set; } = 100;
    public MixVisualOptions Visual { get; set; } = new();
    public double LoopStart { get; set; }
    public double LoopEnd { get; set; } = 8;
    public bool Loop { get; set; }
    public bool AutoSave { get; set; }
    public List<MixLane> Lanes { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public Dictionary<Guid, MixSource> Sources { get; } = [];
    public double Duration => Math.Max(0, Lanes.SelectMany(l => l.Clips).Select(c => c.Start + ClipDuration(c)).DefaultIfEmpty(0).Max());
    public double Rate(MixClip clip) => clip.TempoMode != ClipTempoMode.Original && Sources.TryGetValue(clip.SourceId, out var s)
        && s.Bpm > 0 && float.IsFinite(s.Bpm) ? Math.Clamp(Bpm / s.Bpm, 0.05, 20) : 1;
    public double ClipDuration(MixClip clip) => clip.SourceDuration / Rate(clip);
    public double SnapTime(double seconds, bool shift = false) => Snap || shift
        ? Math.Round(seconds / (240.0 / Bpm / SnapDivision), MidpointRounding.AwayFromZero) * (240.0 / Bpm / SnapDivision)
        : seconds;
    public MixSource AddSource(AudioObj audio)
    {
        if (audio.SampleRate <= 0 || audio.Channels <= 0 || audio.Data.Length % audio.Channels != 0)
            throw new ArgumentException("The audio has an invalid sample format.");
        var source = new MixSource { Name = audio.Name, Data = (float[])audio.Data.Clone(), SampleRate = audio.SampleRate,
            Channels = audio.Channels, Bpm = audio.Bpm > 0 ? audio.Bpm : audio.ScannedBpm };
        Sources.Add(source.Id, source); return source;
    }
    public MixClip AddClip(MixLane lane, MixSource source, double start) => AddClip(lane, source, start, 0, source.Duration);
    public MixClip AddClip(MixLane lane, MixSource source, double start, double offset, double duration)
    {
        offset = Math.Clamp(offset, 0, source.Duration);
        var clip = new MixClip { SourceId = source.Id, Name = source.Name, Start = start,
            SourceOffset = offset, SourceDuration = Math.Clamp(duration, 0, source.Duration - offset) };
        lane.Clips.Add(clip); return clip;
    }
    public string Capture() => JsonSerializer.Serialize(this);
    private static readonly JsonSerializerOptions ProjectJson = CreateProjectJson();
    public string CaptureProject() => JsonSerializer.Serialize(this, ProjectJson);
    public static string SanitizeProjectState(string state) =>
        (JsonSerializer.Deserialize<MixArrangement>(state) ?? throw new InvalidDataException("Invalid arrangement.")).CaptureProject();

    private static JsonSerializerOptions CreateProjectJson()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(type =>
        {
            string[] omitted = type.Type == typeof(MixArrangement) ? [nameof(Loop), nameof(LoopStart), nameof(LoopEnd)] :
                type.Type == typeof(MixVisualOptions) ? [nameof(MixVisualOptions.ScrollMode), nameof(MixVisualOptions.HorizontalScrollSeconds), nameof(MixVisualOptions.FirstVisibleTrack)] : [];
            foreach (var property in type.Properties.Where(property => omitted.Contains(property.Name)).ToArray()) type.Properties.Remove(property);
        });
        return new JsonSerializerOptions { TypeInfoResolver = resolver };
    }

    /// <summary>Active assets plus their transitive pristine tempo origins, for self-contained project saves.</summary>
    public IReadOnlyList<MixSource> ProjectSources()
    {
        var result = new Dictionary<Guid, MixSource>();
        void Add(Guid id, HashSet<Guid> path)
        {
            if (!path.Add(id)) throw new InvalidDataException("Cyclic tempo origin references.");
            if (!Sources.TryGetValue(id, out MixSource? source)) throw new InvalidDataException("Missing tempo origin audio.");
            foreach (Guid dependency in MixTempoRecipe.Dependencies(source))
                if (!result.ContainsKey(dependency)) Add(dependency, path);
            result.TryAdd(id, source); path.Remove(id);
        }
        foreach (Guid id in Lanes.SelectMany(lane => lane.Clips).Select(clip => clip.SourceId).Distinct()) Add(id, []);
        return result.Values.ToArray();
    }
    public void Restore(string json)
    {
        var state = JsonSerializer.Deserialize<MixArrangement>(json) ?? throw new InvalidDataException("Invalid arrangement.");
        Name = state.Name; Bpm = state.Bpm; SnapDivision = state.SnapDivision; Snap = state.Snap; DisplayLaneHeight = state.DisplayLaneHeight; Visual = state.Visual;
        LoopStart = state.LoopStart; LoopEnd = state.LoopEnd; Loop = state.Loop; AutoSave = state.AutoSave; Lanes = state.Lanes;
    }
    public void Validate()
    {
        if (!double.IsFinite(Bpm) || Bpm is < 10 or > 400 || SnapDivision is not (1 or 2 or 4 or 8 or 16 or 32 or 64))
            throw new InvalidDataException("Invalid tempo or snap division.");
        if (DisplayLaneHeight is < 32 or > 600) throw new InvalidDataException("Invalid track display height.");
        if (Visual == null || !double.IsFinite(Visual.Hue) || Visual.Hue is < 0 or > 360
            || !Enum.IsDefined(Visual.ScrollMode)
            || !double.IsFinite(Visual.HueSpeed) || Visual.HueSpeed is < -720 or > 720
            || !double.IsFinite(Visual.PixelsPerSecond) || Visual.PixelsPerSecond is < 8 or > 2000)
            throw new InvalidDataException("Invalid visual options.");
        if (!double.IsFinite(Visual.HorizontalScrollSeconds) || Visual.FirstVisibleTrack < 0)
            throw new InvalidDataException("Invalid viewport position.");
        foreach (var property in typeof(MixVisualOptions).GetProperties().Where(p => p.PropertyType == typeof(string)))
        {
            string? color = property.GetValue(Visual) as string;
            if (color == null || color.Length != 7 || color[0] != '#' || !uint.TryParse(color.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out _)) throw new InvalidDataException("Use #RRGGBB visual colors.");
        }
        if (!double.IsFinite(LoopStart) || !double.IsFinite(LoopEnd) || LoopStart < 0 || LoopEnd < LoopStart)
            throw new InvalidDataException("Invalid loop range.");
        foreach (var s in Sources.Values)
            if (s.SampleRate is < 1000 or > 768000 || s.Channels is < 1 or > 64 || s.Data.Length % s.Channels != 0 || s.Data.Any(v => !float.IsFinite(v)))
                throw new InvalidDataException("Invalid source audio.");
        foreach (var s in Sources.Values)
            if (!double.IsFinite(s.TempoOriginScale) || s.TempoOriginScale <= 0 || !float.IsFinite(s.TempoOriginBpm) || s.TempoOriginBpm < 0 ||
                s.TempoOriginId is Guid origin && (origin == s.Id || !Sources.ContainsKey(origin)))
                throw new InvalidDataException("Invalid or missing tempo origin.");
        foreach (var s in Sources.Values) s.TempoRecipe?.Validate(this.Sources);
        _ = ProjectSources();
        foreach (var lane in Lanes)
        {
            if (!double.IsFinite(lane.Gain) || lane.Gain is < 0 or > 4) throw new InvalidDataException("Invalid track gain.");
            foreach (var c in lane.Clips)
            {
                if (!Sources.TryGetValue(c.SourceId, out var s) || !Enum.IsDefined(c.TempoMode)
                    || !double.IsFinite(c.Start) || new[] { c.SourceOffset, c.SourceDuration, c.Gain, c.FadeIn, c.FadeOut }.Any(v => !double.IsFinite(v) || v < 0)
                    || c.Gain > 4 || c.SourceOffset + c.SourceDuration > s.Duration + 0.0001)
                    throw new InvalidDataException("Invalid audio segment.");
            }
        }
    }
    public Task SaveAsync(string path, CancellationToken token = default) => MixProjectFile.SaveAsync(path, this, token);
    public static Task<MixArrangement> LoadAsync(string path, CancellationToken token = default) => MixProjectFile.LoadAsync(path, token);
    private async Task SaveLegacyAsync(string path, CancellationToken token = default)
    {
        Validate();
        string metadata = Capture(); var sources = Sources.Values.ToArray();
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await Task.Run(() =>
            {
                using var file = File.Create(temp);
                using var zip = new ZipArchive(file, ZipArchiveMode.Create);
                using (var writer = new StreamWriter(zip.CreateEntry("arrangement.json").Open())) writer.Write(metadata);
                foreach (var s in sources)
                {
                    token.ThrowIfCancellationRequested();
                    using (var writer = new StreamWriter(zip.CreateEntry($"sources/{s.Id}.json").Open())) writer.Write(JsonSerializer.Serialize(s));
                    using var stream = zip.CreateEntry($"sources/{s.Id}.f32", CompressionLevel.Fastest).Open();
                    byte[] bytes = new byte[65536 * sizeof(float)];
                    for (int i = 0; i < s.Data.Length; i += 65536)
                    {
                        token.ThrowIfCancellationRequested();
                        int count = Math.Min(65536, s.Data.Length - i);
                        Buffer.BlockCopy(s.Data, i * sizeof(float), bytes, 0, count * sizeof(float)); stream.Write(bytes, 0, count * sizeof(float));
                    }
                }
            }, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static Task<MixArrangement> LoadLegacyAsync(string path, CancellationToken token = default) => Task.Run(() =>
    {
        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry("arrangement.json")?.Open() ?? throw new InvalidDataException("No arrangement metadata."));
        var mix = JsonSerializer.Deserialize<MixArrangement>(reader.ReadToEnd()) ?? throw new InvalidDataException("Invalid project.");
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("sources/", StringComparison.Ordinal) && e.FullName.EndsWith(".json", StringComparison.Ordinal)))
        {
            token.ThrowIfCancellationRequested();
            using var sourceReader = new StreamReader(entry.Open());
            var source = JsonSerializer.Deserialize<MixSource>(sourceReader.ReadToEnd()) ?? throw new InvalidDataException("Invalid source.");
            var pcm = zip.GetEntry($"sources/{source.Id}.f32") ?? throw new InvalidDataException("Missing embedded audio.");
            if (pcm.Length % 4 != 0 || pcm.Length > int.MaxValue) throw new InvalidDataException("Invalid audio size.");
            source.Data = new float[(int)(pcm.Length / 4)];
            using var stream = pcm.Open();
            byte[] bytes = new byte[65536 * 4];
            for (int i = 0; i < source.Data.Length; i += 65536)
            {
                token.ThrowIfCancellationRequested(); int count = Math.Min(65536, source.Data.Length - i);
                stream.ReadExactly(bytes.AsSpan(0, count * 4)); Buffer.BlockCopy(bytes, 0, source.Data, i * 4, count * 4);
            }
            mix.Sources.Add(source.Id, source);
        }
        mix.Validate(); return mix;
    }, token);
    public void Dispose() { Sources.Clear(); Lanes.Clear(); }
}
