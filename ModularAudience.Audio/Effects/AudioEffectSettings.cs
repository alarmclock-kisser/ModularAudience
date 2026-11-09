namespace ModularAudience.Audio.Effects;

public enum AudioEffectKind
{
    AutoTune, PitchShift, TimeStretch,
    RoomReverb, HallReverb, PlateReverb, SpringReverb, ChamberReverb, ShimmerReverb,
    SingleDelay, SlapbackDelay, PingPongDelay, MultiTapDelay,
    Compressor, Limiter, Gate, Expander, DeEsser,
    Bitcrusher, SampleRateReducer, Overdrive, Saturation, TapeSaturation,
    Chorus, Flanger, Phaser, Tremolo,
    Gain, Normalize, FadeIn, FadeOut, StereoWidth, ConvertToMono, Reverse,
    HumRemoval, NoiseReduction
}

public enum EffectParameterKind { Number, Choice, Boolean }

/// <summary>One schema drives validation, the parameter editor and persisted presets.</summary>
public sealed record EffectParameter(string Key, string Label, double Minimum, double Maximum, double DefaultValue,
    double Increment = 1, bool Integral = false, EffectParameterKind Kind = EffectParameterKind.Number,
    IReadOnlyList<string>? Choices = null, string Description = "");

/// <summary>
/// Choice values are stable, zero-based indices into the effect's schema; booleans are 0/1.
/// Copies deliberately own their dictionary: editing a dialog never changes a factory preset.
/// </summary>
public sealed record AudioEffectSettings
{
    public required AudioEffectKind Effect { get; init; }
    public Dictionary<string, double> Parameters { get; init; } = new(StringComparer.Ordinal);
    public double this[string key] => this.Parameters[key];
    public bool Flag(string key) => this[key] != 0;
    public int Choice(string key) => checked((int)this[key]);
    public AudioEffectSettings Copy() => this with { Parameters = new(this.Parameters, StringComparer.Ordinal) };

    public AudioEffectSettings With(params (string Key, double Value)[] changes)
    {
        AudioEffectSettings copy = this.Copy();
        foreach (var (key, value) in changes) copy.Parameters[key] = value;
        copy.Validate();
        return copy;
    }

    public void Validate()
    {
        AudioEffectDefinition definition = AudioEffectCatalog.Get(this.Effect);
        if (this.Effect == AudioEffectKind.AutoTune) throw new ArgumentException("Auto Tune uses its own typed settings.");
        if (this.Parameters is null || this.Parameters.Count != definition.Parameters.Count)
            throw new ArgumentException("The preset does not contain the complete parameter set for this effect.");
        foreach (EffectParameter parameter in definition.Parameters)
        {
            if (!this.Parameters.TryGetValue(parameter.Key, out double value) || !double.IsFinite(value) ||
                value < parameter.Minimum || value > parameter.Maximum || (parameter.Integral && value != Math.Truncate(value)))
                throw new ArgumentOutOfRangeException(parameter.Key, $"{parameter.Label}: expected {parameter.Minimum}–{parameter.Maximum}.");
        }
        if (this.Effect == AudioEffectKind.Phaser && this["MinimumHz"] >= this["MaximumHz"])
            throw new ArgumentException("The phaser's maximum frequency must be above its minimum frequency.");
    }

    public bool SoundEquals(AudioEffectSettings? other) => other is not null && this.Effect == other.Effect &&
        this.Parameters.Count == other.Parameters.Count && this.Parameters.All(pair =>
            other.Parameters.TryGetValue(pair.Key, out double value) && pair.Value == value);
}

public sealed record AudioEffectResult(float[] Samples, int Channels, double? TempoMultiplier = null);

public sealed record EffectPresetDefinition(string Id, string Name, AudioEffectSettings Parameters);

public sealed class AudioEffectDefinition
{
    internal AudioEffectDefinition(AudioEffectKind kind, string group, string name, string description, EffectParameter[] parameters,
        params (string Id, string Name, (string Key, double Value)[] Changes)[] presets)
    {
        this.Kind = kind; this.Group = group; this.Name = name; this.Description = description;
        this.Parameters = Array.AsReadOnly(parameters);
        this.BuiltInPresets = kind is AudioEffectKind.AutoTune or AudioEffectKind.ConvertToMono or AudioEffectKind.Reverse
            ? [] : Array.AsReadOnly(new[] { new EffectPresetDefinition("default", "Balanced", this.CreateDefaults()) }
                .Concat(presets.Select(preset => new EffectPresetDefinition(preset.Id, preset.Name, this.CreateDefaultsUnchecked(preset.Changes)))).ToArray());
    }

    public AudioEffectKind Kind { get; }
    public string Group { get; }
    public string Name { get; }
    public string Description { get; }
    public bool OpensOptions => this.Kind is not (AudioEffectKind.ConvertToMono or AudioEffectKind.Reverse);
    public IReadOnlyList<EffectParameter> Parameters { get; }
    public IReadOnlyList<EffectPresetDefinition> BuiltInPresets { get; }
    public AudioEffectSettings CreateDefaults() => this.CreateDefaultsUnchecked([]);

    private AudioEffectSettings CreateDefaultsUnchecked((string Key, double Value)[] changes)
    {
        var settings = new AudioEffectSettings { Effect = this.Kind, Parameters = this.Parameters.ToDictionary(p => p.Key, p => p.DefaultValue, StringComparer.Ordinal) };
        // Do not call the catalog while its static initializer is building the definitions.
        foreach (var (key, value) in changes)
        {
            if (!settings.Parameters.ContainsKey(key)) throw new ArgumentException("Unknown factory-preset parameter: " + key);
            settings.Parameters[key] = value;
        }
        return settings;
    }
}
