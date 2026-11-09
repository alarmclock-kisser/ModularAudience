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

    public bool Apply(AudioObj target, float[] processed) => this.Apply(target, new AudioEffectResult(processed, this.Channels));

    public bool Apply(AudioObj target, AudioEffectResult result)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(result);
        float[] processed = result.Samples;
        if (processed is null || processed.Length == 0 || result.Channels <= 0 || processed.Length % result.Channels != 0 ||
            processed.Any(value => !float.IsFinite(value)) ||
            (result.TempoMultiplier is double tempo && (!double.IsFinite(tempo) || tempo <= 0)))
            throw new ArgumentException("Effect output must contain finite, complete audio frames.", nameof(result));
        if (!this.IsCurrent(target)) throw new InvalidOperationException("The track changed while the effect window was open. Close it and reopen the effect for the current audio.");

        int outputChannels = this.IsSelection ? this.Channels : result.Channels;
        int renderedFrames = processed.Length / result.Channels;
        if (this.IsSelection && result.Channels != this.Channels)
        {
            if (result.Channels != 1) throw new ArgumentException("A selected region cannot change the track's channel format.");
            // A channel format cannot change in the middle of a track. A selected
            // mono conversion therefore writes the mono average to every channel.
            float[] expanded = new float[checked(renderedFrames * this.Channels)];
            for (int frame = 0; frame < renderedFrames; frame++)
                expanded.AsSpan(frame * this.Channels, this.Channels).Fill(processed[frame]);
            processed = expanded;
        }
        if (outputChannels == this.Channels &&
            MemoryMarshal.AsBytes(processed.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(this.input.AsSpan()))) return false;

        int suffixStart = this.StartSample + this.SampleCount;
        int suffixLength = this.IsSelection ? this.originalData.Length - suffixStart : 0;
        int prefixLength = this.IsSelection ? this.StartSample : 0;
        float[] replacement = new float[checked(prefixLength + processed.Length + suffixLength)];
        this.originalData.AsSpan(0, prefixLength).CopyTo(replacement);
        processed.CopyTo(replacement, prefixLength);
        if (suffixLength > 0) this.originalData.AsSpan(suffixStart).CopyTo(replacement.AsSpan(prefixLength + processed.Length));

        long position = target.Position;
        long selectionStart = target.SelectionStart, selectionEnd = target.SelectionEnd;
        long scroll = target.ScrollOffset, starting = target.StartingOffset;
        long loopStart = target.LoopStartSamples, loopEnd = target.LoopEndSamples;
        bool looping = target.LoopEnabled;
        int oldFrames = this.originalData.Length / this.Channels;
        int newFrames = replacement.Length / outputChannels;
        bool[]? grid = target.BeatGrid.Length == oldFrames && oldFrames != newFrames ? new bool[newFrames] : null;
        if (grid is not null)
            for (int frame = 0; frame < target.BeatGrid.Length; frame++)
                if (target.BeatGrid[frame]) grid[(int)Math.Min(newFrames - 1, this.MapFrame(frame, renderedFrames))] = true;

        target.CreateUndoStep();
        target.Data = replacement;
        target.Channels = outputChannels;
        target.Length = replacement.LongLength;
        target.Duration = TimeSpan.FromSeconds(newFrames / (double)this.SampleRate);
        if (this.Channels != outputChannels || this.SampleCount / this.Channels != renderedFrames)
        {
            long MapSample(long sample) => sample < 0 ? -1 : this.MapFrame(sample / this.Channels, renderedFrames) * outputChannels;
            target.SelectionStart = MapSample(selectionStart); target.SelectionEnd = MapSample(selectionEnd);
            target.ScrollOffset = MapSample(scroll); target.StartingOffset = MapSample(starting);
            target.SetPosition(this.MapFrame(position, renderedFrames));
            long mappedLoopStart = Math.Max(0, MapSample(loopStart)), mappedLoopEnd = Math.Max(0, MapSample(loopEnd));
            target.UpdateLoopFraction(mappedLoopStart, mappedLoopEnd, Math.Max(0, mappedLoopEnd - mappedLoopStart),
                looping && mappedLoopEnd > mappedLoopStart, adjustPosition: false);
            if (grid is not null) target.BeatGrid = grid;
        }
        if (!this.IsSelection && result.TempoMultiplier is double multiplier)
        {
            if (float.IsFinite(target.Bpm) && target.Bpm > 0) target.Bpm = (float)(target.Bpm * multiplier);
            if (float.IsFinite(target.ScannedBpm) && target.ScannedBpm > 0) target.ScannedBpm = (float)(target.ScannedBpm * multiplier);
        }
        target.InvalidateWaveformCache();
        return true;
    }

    /// <summary>Piecewise ripple mapping: before is fixed, inside scales, after shifts by the duration delta.</summary>
    public long MapFrame(long frame, int renderedFrames)
    {
        long start = this.StartSample / this.Channels, count = this.SampleCount / this.Channels;
        long mapped = frame <= start ? frame : frame >= start + count
            ? frame + renderedFrames - count : start + (long)Math.Round((frame - start) * (renderedFrames / (double)count));
        return Math.Clamp(mapped, 0, this.originalData.LongLength / this.Channels - count + renderedFrames);
    }

    private static byte[] Hash(float[] data) => SHA256.HashData(MemoryMarshal.AsBytes(data.AsSpan()));
}
