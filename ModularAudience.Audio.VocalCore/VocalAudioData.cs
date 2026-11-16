namespace ModularAudience.Audio.Vocal;

public sealed class VocalAudioData
{
    public float[] Data { get; init; } = [];
    public int SampleRate { get; init; }
    public int Channels { get; init; }
    public int BitDepth { get; init; }
    public long Length { get; init; }
    public TimeSpan Duration { get; init; }
    public float Bpm { get; set; }
    public float Volume { get; init; }
    public string Name { get; private set; } = string.Empty;

    public void Rename(string name) => Name = name;
}
