using ModularAudience.Audio;
using ModularAudience.Audio.Processing;
using ModularAudience.Quantum.Models;

namespace ModularAudience.Forms.Quantum;

internal static class QuantumSequenceAudioRenderer
{
    private const int MaximumSamples = 67_108_864;

    internal static async Task<AudioObj> RenderAsync(QuantumBeatSequence sequence, IReadOnlyList<AudioObj> samples, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(samples);
        sequence.Options.Validate(sequence.QubitCount, sequence.Tracks);
        if (samples.Count != sequence.Tracks.Count || samples.Any(sample => sample is null || sample.SampleRate <= 0 || sample.Channels is < 1 or > 8 ||
            sample.Data.Length < sample.Channels || sample.Data.Length % sample.Channels != 0) || samples.Sum(sample => (long)sample.Data.Length) > MaximumSamples)
            throw new ArgumentException("Select valid samples with complete frames; combined source PCM must not exceed 256 MiB.");
        token.ThrowIfCancellationRequested();
        var snapshots = new List<AudioObj>();
        try
        {
            foreach (var sample in samples) snapshots.Add(sample.Clone());
            return await Task.Run(() => Render(sequence, snapshots, token), token).ConfigureAwait(false);
        }
        finally { foreach (var sample in snapshots) sample.Dispose(); }
    }

    private static AudioObj Render(QuantumBeatSequence sequence, IReadOnlyList<AudioObj> samples, CancellationToken token)
    {
        const int rate = 44100, channels = 2;
        var settings = sequence.Options;
        double secondsPerStep = 240 / settings.Bpm / settings.StepsPerBar;
        int frames = checked((int)Math.Ceiling(settings.Bars * settings.StepsPerBar * secondsPerStep * rate));
        if (frames * (long)channels > MaximumSamples) throw new ArgumentException("The quantum arrangement exceeds the 256 MiB output limit.");
        var mix = new float[frames * channels];
        var cache = new Dictionary<(int Track, double Pitch, double Stretch, double Speed, double Cut, bool Reverse, double Duration), AudioObj>();
        long retainedSamples = 0;
        try
        {
            foreach (var note in sequence.Notes)
            {
                token.ThrowIfCancellationRequested();
                if (note.TrackIndex < 0 || note.TrackIndex >= samples.Count || !double.IsFinite(note.StartStep) || note.StartStep < 0 ||
                    note.StartStep >= settings.Bars * settings.StepsPerBar || !double.IsFinite(note.Velocity) || note.Velocity is < 0 or > 1 ||
                    !double.IsFinite(note.DurationSteps) || note.DurationSteps is <= 0 or > 16 || !double.IsFinite(note.PitchShiftCents) || Math.Abs(note.PitchShiftCents) > 2400 ||
                    !double.IsFinite(note.TimeStretchFactor) || note.TimeStretchFactor is < 0.5 or > 2 || !double.IsFinite(note.VarispeedFactor) || note.VarispeedFactor is < 0.5 or > 2 ||
                    !double.IsFinite(note.SliceCutOffsetMs) || note.SliceCutOffsetMs < 0)
                    throw new ArgumentException("The quantum sequence contains an invalid audio event.");
                var key = (note.TrackIndex, note.PitchShiftCents, note.TimeStretchFactor, note.VarispeedFactor, note.SliceCutOffsetMs, note.Reverse,
                    settings.FitToNoteDuration || settings.TruncateNotes ? note.DurationSteps : 0);
                if (!cache.TryGetValue(key, out var clip))
                {
                    AudioObj source = samples[note.TrackIndex];
                    int sourceFrames = source.Data.Length / source.Channels;
                    int cutFrame = (int)Math.Clamp(Math.Floor(note.SliceCutOffsetMs * source.SampleRate / 1000), 0, sourceFrames - 1);
                    double estimate = (sourceFrames - cutFrame) * note.TimeStretchFactor / note.VarispeedFactor;
                    if (settings.FitToNoteDuration) estimate = note.DurationSteps * secondsPerStep * source.SampleRate;
                    if (estimate * source.Channels > MaximumSamples - retainedSamples)
                        throw new ArgumentException("Quantum DSP variations exceed the 256 MiB preparation limit. Use shorter samples or fewer variations.");
                    using var cut = new AudioObj { Data = source.Data[(cutFrame * source.Channels)..], SampleRate = source.SampleRate, Channels = source.Channels, BitDepth = 32 };
                    if (cut.Data.Any(value => !float.IsFinite(value))) throw new ArgumentException("Quantum audio requires finite PCM samples.");
                    var chain = new SampleModellerSettings();
                    if (note.Reverse) chain.Effects.Add(new SampleEffect { Kind = SampleEffectKind.Reverse });
                    if (note.PitchShiftCents != 0) chain.Effects.Add(new SampleEffect { Kind = SampleEffectKind.PitchShift, Semitones = note.PitchShiftCents / 100 });
                    if (note.TimeStretchFactor != 1) chain.Effects.Add(new SampleEffect { Kind = SampleEffectKind.TimeStretch, Factor = note.TimeStretchFactor });
                    if (note.VarispeedFactor != 1) chain.Effects.Add(new SampleEffect { Kind = SampleEffectKind.Varispeed, Factor = note.VarispeedFactor });
                    clip = SampleModellerDsp.Render(cut, chain, token);
                    cache.Add(key, clip);
                    int requested = Math.Max(1, checked((int)Math.Ceiling(note.DurationSteps * secondsPerStep * clip.SampleRate)));
                    if (settings.FitToNoteDuration)
                    {
                        clip.Data = ModularAudience.Audio.Processors_V1.PitchShifter.StretchPhaseVocoder(clip.Data, clip.Channels, requested, cancellationToken: token);
                    }
                    else if (settings.TruncateNotes && requested < clip.Data.Length / clip.Channels)
                        clip.Data = clip.Data[..(requested * clip.Channels)];
                    retainedSamples += clip.Data.Length;
                }

                int start = (int)Math.Round(note.StartStep * secondsPerStep * rate);
                double ratio = clip.SampleRate / (double)rate;
                int inputFrames = clip.Data.Length / clip.Channels;
                int outputFrames = checked((int)Math.Ceiling(inputFrames / ratio));
                long required = ((long)start + outputFrames) * channels;
                if (settings.KeepEndTails && required > mix.Length)
                {
                    if (required > MaximumSamples) throw new ArgumentException("Quantum note tails exceed the output memory limit.");
                    Array.Resize(ref mix, (int)required);
                }
                outputFrames = Math.Min(outputFrames, mix.Length / channels - start);
                for (int frame = 0; frame < outputFrames; frame++)
                {
                    if ((frame & 4095) == 0) token.ThrowIfCancellationRequested();
                    double position = frame * ratio;
                    int first = Math.Min(inputFrames - 1, (int)position), second = Math.Min(inputFrames - 1, first + 1);
                    double fraction = position - first;
                    for (int channel = 0; channel < channels; channel++)
                    {
                        float a = clip.Data[first * clip.Channels + channel % clip.Channels], b = clip.Data[second * clip.Channels + channel % clip.Channels];
                        mix[(start + frame) * channels + channel] += (float)((a + (b - a) * fraction) * note.Velocity);
                    }
                }
            }
            double peak = 0;
            for (int i = 0; i < mix.Length; i++)
            {
                if ((i & 65535) == 0) token.ThrowIfCancellationRequested();
                if (!float.IsFinite(mix[i])) throw new ArithmeticException("Quantum rendering produced non-finite audio.");
                peak = Math.Max(peak, Math.Abs((double)mix[i]));
            }
            double gain = Math.Pow(10, settings.OutputGainDb / 20) * (settings.NormalizeOutput && peak > 0 ? 0.95 / peak : 1);
            for (int i = 0; i < mix.Length; i++)
            {
                if ((i & 65535) == 0) token.ThrowIfCancellationRequested();
                mix[i] = (float)Math.Clamp(mix[i] * gain, -1, 1);
            }
            var result = new AudioObj { Data = mix, SampleRate = rate, Channels = channels, BitDepth = 32, Bpm = (float)settings.Bpm,
                Length = mix.Length, Duration = TimeSpan.FromSeconds(mix.Length / (double)(rate * channels)) };
            result.Rename("Quantum Breakbeat " + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            return result;
        }
        finally { foreach (var clip in cache.Values) clip.Dispose(); }
    }
}
