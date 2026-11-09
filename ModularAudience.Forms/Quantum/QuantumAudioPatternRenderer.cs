using ModularAudience.Audio;
using ModularAudience.Audio.Processing;
using ModularAudience.Quantum.Models;

namespace ModularAudience.Forms.Quantum;

/// <summary>Prepares DSP off the audio callback. Velocity is baked into the private PCM, never into the source.</summary>
internal static class QuantumAudioPatternRenderer
{
    private const int MaximumPreparedSamples = 67_108_864;

    internal static async Task<PreparedQuantumPattern> PrepareAsync(AudioObj source, QuantumPatternResult pattern,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(pattern.Slices);
        cancellationToken.ThrowIfCancellationRequested();
        if (source.SampleRate <= 0 || source.Channels <= 0 || source.Data.Length < source.Channels || source.Data.Length % source.Channels != 0)
            throw new ArgumentException("Quantum patterns require a sample with complete audio frames and a valid format.");
        if (source.Data.Length > MaximumPreparedSamples)
            throw new ArgumentException("The sample exceeds the 256 MiB quantum preparation limit. Use a shorter slice.");
        DrumSliceTrigger[] slices = pattern.Slices.ToArray();
        if (slices.Length is < 1 or > 1024 ||
            slices.Any(slice => slice is null || slice.StepIndex < 0 || slice.StepIndex >= slices.Length ||
                !double.IsFinite(slice.Velocity) || slice.Velocity is < 0 or > 1 ||
                !double.IsFinite(slice.PitchShiftCents) || slice.PitchShiftCents is < -1200 or > 1200 ||
                !double.IsFinite(slice.TimeStretchFactor) || slice.TimeStretchFactor is < 0.5 or > 2 ||
                !double.IsFinite(slice.SliceCutOffsetMs) || slice.SliceCutOffsetMs < 0) ||
            slices.Select(slice => slice.StepIndex).Distinct().Count() != slices.Length)
            throw new ArgumentException("The quantum pattern contains invalid step indices or DSP parameters.");

        using AudioObj snapshot = source.Clone();
        return await Task.Run(() => Prepare(snapshot, slices, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static PreparedQuantumPattern Prepare(AudioObj source, DrumSliceTrigger[] slices, CancellationToken token)
    {
        var samples = new Dictionary<int, AudioObj>();
        var cache = new Dictionary<(double Velocity, double Pitch, double Stretch, double Cut), AudioObj>();
        int preparedSamples = 0;
        try
        {
            for (int i = 0; i < source.Data.Length; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                if (!float.IsFinite(source.Data[i])) throw new ArgumentException("Quantum patterns require finite source audio samples.");
            }
            foreach (DrumSliceTrigger slice in slices.Where(slice => slice.IsTriggered))
            {
                token.ThrowIfCancellationRequested();
                var key = (slice.Velocity, slice.PitchShiftCents, slice.TimeStretchFactor, slice.SliceCutOffsetMs);
                if (!cache.TryGetValue(key, out AudioObj? rendered))
                {
                    int frames = source.Data.Length / source.Channels;
                    int cutFrame = (int)Math.Clamp(Math.Floor(slice.SliceCutOffsetMs * source.SampleRate / 1000), 0, frames - 1);
                    double estimate = Math.Ceiling((frames - cutFrame) * slice.TimeStretchFactor) * source.Channels;
                    if (estimate > MaximumPreparedSamples - preparedSamples)
                        throw new ArgumentException("Prepared quantum slices would exceed 256 MiB. Use a shorter sample or fewer variations.");
                    using var cut = new AudioObj
                    {
                        Data = source.Data[(cutFrame * source.Channels)..], SampleRate = source.SampleRate,
                        Channels = source.Channels, BitDepth = source.BitDepth, Bpm = source.Bpm,
                        Key = source.Key, SampleTag = source.SampleTag, Tag = source.Tag
                    };
                    cut.Rename(source.Name + " [Quantum]");
                    var settings = new SampleModellerSettings();
                    if (slice.PitchShiftCents != 0)
                        settings.Effects.Add(new SampleEffect { Kind = SampleEffectKind.PitchShift, Semitones = slice.PitchShiftCents / 100 });
                    if (slice.TimeStretchFactor != 1)
                        settings.Effects.Add(new SampleEffect { Kind = SampleEffectKind.TimeStretch, Factor = slice.TimeStretchFactor });
                    rendered = SampleModellerDsp.Render(cut, settings, token);
                    // Track ownership immediately, including when cancellation occurs in the gain pass.
                    cache.Add(key, rendered);
                    preparedSamples = checked(preparedSamples + rendered.Data.Length);
                    for (int i = 0; i < rendered.Data.Length; i++)
                    {
                        if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                        rendered.Data[i] *= (float)slice.Velocity;
                    }
                }
                samples.Add(slice.StepIndex, rendered);
            }
            token.ThrowIfCancellationRequested();
            var steps = new bool[slices.Length];
            foreach (DrumSliceTrigger slice in slices) steps[slice.StepIndex] = slice.IsTriggered;
            return new(steps, samples);
        }
        catch
        {
            foreach (AudioObj audio in cache.Values) audio.Dispose();
            throw;
        }
    }
}

internal sealed class PreparedQuantumPattern(bool[] steps, IReadOnlyDictionary<int, AudioObj> samples) : IDisposable
{
    internal bool[] Steps { get; } = steps;
    internal IReadOnlyDictionary<int, AudioObj> Samples { get; } = samples;

    public void Dispose()
    {
        foreach (AudioObj audio in this.Samples.Values.Distinct()) audio.Dispose();
    }
}
