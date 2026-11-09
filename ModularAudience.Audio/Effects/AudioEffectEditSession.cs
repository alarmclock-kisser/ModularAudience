using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ModularAudience.Audio.Effects;

/// <summary>
/// Frozen, frame-aligned effect input. Commit is synchronous: stale edits are
/// rejected before one undo snapshot and one sample-array replacement occur.
/// </summary>
public sealed class AudioEffectEditSession
{
    private readonly float[] originalData;
    private readonly byte[] contentHash;
    private readonly float[] input;

    private AudioEffectEditSession(AudioObj source)
    {
        if (source.SampleRate <= 0 || source.Channels <= 0 || source.Data.Length == 0 || source.Data.Length % source.Channels != 0)
            throw new ArgumentException("The effect needs non-empty, complete audio frames.", nameof(source));
        this.originalData = source.Data;
        this.SampleRate = source.SampleRate;
        this.Channels = source.Channels;
        bool selected = source.SelectionStart >= 0 && source.SelectionEnd > source.SelectionStart;
        long start = selected ? Math.Clamp(source.SelectionStart, 0, source.Data.LongLength) : 0;
        long end = selected ? Math.Clamp(source.SelectionEnd, 0, source.Data.LongLength) : source.Data.LongLength;
        if (start % this.Channels != 0 || end % this.Channels != 0 || end <= start)
            throw new ArgumentException("The selected range must contain complete audio frames.");
        this.StartSample = checked((int)start);
        this.input = this.originalData.AsSpan(this.StartSample, checked((int)(end - start))).ToArray();
        this.contentHash = Hash(this.originalData);
        this.IsSelection = selected;
    }

    public int SampleRate { get; }
    public int Channels { get; }
    public int StartSample { get; }
    public int SampleCount => this.input.Length;
    public bool IsSelection { get; }
    public ReadOnlyMemory<float> Input => this.input;

    public static AudioEffectEditSession Capture(AudioObj source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new AudioEffectEditSession(source);
    }

    public bool IsCurrent(AudioObj target) => ReferenceEquals(target.Data, this.originalData) &&
        target.SampleRate == this.SampleRate && target.Channels == this.Channels &&
        CryptographicOperations.FixedTimeEquals(this.contentHash, Hash(target.Data)) &&
        MemoryMarshal.AsBytes(target.Data.AsSpan(this.StartSample, this.input.Length)).SequenceEqual(MemoryMarshal.AsBytes(this.input.AsSpan()));

    public bool Apply(AudioObj target, float[] processed)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(processed);
        if (processed.Length != this.input.Length || processed.Any(value => !float.IsFinite(value)))
            throw new ArgumentException("Effect output must contain the same number of finite samples.", nameof(processed));
        if (!this.IsCurrent(target)) throw new InvalidOperationException("The track changed while the effect window was open. Close it and reopen the effect for the current audio.");
        if (MemoryMarshal.AsBytes(processed.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(this.input.AsSpan()))) return false;
        float[] replacement = (float[])target.Data.Clone();
        processed.CopyTo(replacement, this.StartSample);
        target.CreateUndoStep();
        target.Data = replacement;
        target.InvalidateWaveformCache();
        return true;
    }

    private static byte[] Hash(float[] data) => SHA256.HashData(MemoryMarshal.AsBytes(data.AsSpan()));
}
