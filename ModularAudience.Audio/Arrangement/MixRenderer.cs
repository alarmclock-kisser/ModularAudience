using ModularAudience.Audio.Processors_V1;
using ModularAudience.Audio.Processing;

namespace ModularAudience.Audio.Arrangement;

public static class MixRenderer
{
    public static Task<MixSource> PrepareSourceAsync(MixSource source, double targetBpm, ClipTempoMode mode, CancellationToken token = default,
        PlaylistStretchSettings? stretchSettings = null) =>
        Task.Run(async () =>
        {
            if (mode == ClipTempoMode.Original || source.Bpm <= 0 || !float.IsFinite(source.Bpm)) return source;
            double factor = Math.Clamp(targetBpm / source.Bpm, 0.05, 20);
            token.ThrowIfCancellationRequested();
            if (mode == ClipTempoMode.TimeStretch && stretchSettings != null)
            {
                return await PrepareConfiguredAsync(source, targetBpm, stretchSettings, token).ConfigureAwait(false);
            }
            float[] data = Resample(source.Data, source.Channels, source.SampleRate * factor, 44100);
            if (mode == ClipTempoMode.TimeStretch && Math.Abs(factor - 1) > 0.00001)
            {
                using var input = new AudioObj { Data = data, Channels = source.Channels, SampleRate = 44100 };
                using var shifted = await PitchShifter.CreatePitchShiftWithoutTimestretchAsync(input,
                    (float)(-12 * Math.Log2(factor)), maxWorkers: Environment.ProcessorCount);
                data = shifted.Data;
            }
            token.ThrowIfCancellationRequested();
            return new MixSource { Name = source.Name, Data = data, Channels = source.Channels, SampleRate = 44100, Bpm = (float)targetBpm };
        }, token);

    private static async Task<MixSource> PrepareConfiguredAsync(MixSource source, double targetBpm, PlaylistStretchSettings settings, CancellationToken token)
    {
        int frames = source.Data.Length / source.Channels;
        if (frames == 0) return new MixSource { Name = source.Name, Data = [], Channels = source.Channels, SampleRate = source.SampleRate, Bpm = (float)targetBpm };
        double factor = source.Bpm / targetBpm, requested = Math.Max(1, Math.Round(frames * factor));
        if (!double.IsFinite(requested) || requested * source.Channels > 134_217_728) throw new InvalidOperationException("The stretched source exceeds 512 MiB.");
        int targetFrames = (int)requested; float[] output = new float[checked(targetFrames * source.Channels)];
        for (int channel = 0; channel < source.Channels; channel++)
        {
            token.ThrowIfCancellationRequested(); var mono = new float[frames];
            for (int frame = 0; frame < frames; frame++) mono[frame] = source.Data[frame * source.Channels + channel];
            using var input = new AudioObj { Data = mono, Channels = 1, SampleRate = source.SampleRate, Length = mono.Length, Bpm = source.Bpm };
            if (settings.UseV2)
                await global::TimeStretcher_V2.Timestretch_V2Async(input, factor,
                    settings.AutoChunking ? null : settings.ChunkSize, settings.AutoChunking ? null : settings.Overlap, null, token).ConfigureAwait(false);
            else
            {
                var result = await TimeStretcher.TimeStretchAllThreadsAsync(input, settings.ChunkSize, settings.Overlap, factor,
                    false, 0, Math.Clamp(settings.Threads, 1, Environment.ProcessorCount), null, settings.Offload, settings.Channeled).ConfigureAwait(false);
                if (!ReferenceEquals(result, input)) input.ReplaceWith(result, disposeSource: true);
            }
            token.ThrowIfCancellationRequested();
            for (int frame = 0; frame < Math.Min(targetFrames, input.Data.Length); frame++) output[frame * source.Channels + channel] = input.Data[frame];
        }
        // The legacy V2 routine is scalar, not interleaved-stereo DSP. Isolating
        // channels above prevents channel scrambling and odd sample counts.
        // One linked level correction retains the source's stereo balance.
        double before = source.Data.Max(value => Math.Abs((double)value)), after = output.Max(value => Math.Abs((double)value));
        if (before > 0 && after > 1e-12)
            for (int i = 0; i < output.Length; i++) { if ((i & 65535) == 0) token.ThrowIfCancellationRequested(); output[i] = (float)(output[i] * before / after); }
        return new MixSource { Name = source.Name, Data = output, Channels = source.Channels, SampleRate = source.SampleRate, Bpm = (float)targetBpm };
    }

    public static Task<AudioObj> RenderAsync(MixArrangement arrangement, CancellationToken token = default, IProgress<double>? progress = null,
        bool normalizeOutput = true)
    {
        arrangement.Validate();
        var snapshot = new MixArrangement(); snapshot.Restore(arrangement.Capture());
        foreach (var pair in arrangement.Sources) snapshot.Sources.Add(pair.Key, pair.Value);
        return Task.Run(async () =>
        {
            const int rate = 44100, channels = 2;
            long totalFrames = (long)Math.Ceiling(Math.Max(0.01, snapshot.Duration) * rate);
            if (totalFrames * channels > int.MaxValue) throw new InvalidOperationException("The mix exceeds the RAM audio buffer limit.");
            float[] output = new float[checked((int)totalFrames * channels)];
            bool solo = snapshot.Lanes.Any(l => l.Solo);
            var jobs = snapshot.Lanes.Where(l => !l.Mute && (!solo || l.Solo)).SelectMany(l => l.Clips
                .Where(c => c.Start + snapshot.ClipDuration(c) > 0).Select(c => (Lane: l, Clip: c))).ToArray();
            int done = 0;
            // Track transformations run in parallel, summation is protected and gain stays floating point.
            object gate = new();
            await Parallel.ForEachAsync(jobs, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8) }, async (job, ct) =>
            {
                var c = job.Clip; var s = snapshot.Sources[c.SourceId]; int sourceChannels = s.Channels;
                int begin = checked((int)Math.Min(s.Data.LongLength / sourceChannels, Math.Round(c.SourceOffset * s.SampleRate)) * sourceChannels);
                int count = checked((int)Math.Min((s.Data.LongLength - begin) / sourceChannels, Math.Round(c.SourceDuration * s.SampleRate)) * sourceChannels);
                float[] data = s.Data.AsSpan(begin, count).ToArray();
                double factor = snapshot.Rate(c);
                if (c.TempoMode == ClipTempoMode.TimeStretch && Math.Abs(factor - 1) > 0.00001)
                {
                    // First varispeed to target duration, then compensate pitch without changing duration.
                    data = Resample(data, sourceChannels, s.SampleRate * factor, rate);
                    using var input = new AudioObj { Data = data, Channels = sourceChannels, SampleRate = rate };
                    using var shifted = await PitchShifter.CreatePitchShiftWithoutTimestretchAsync(input, (float)(-12 * Math.Log2(factor)), maxWorkers: 1);
                    data = shifted.Data;
                }
                else data = Resample(data, sourceChannels, s.SampleRate * factor, rate);
                ct.ThrowIfCancellationRequested(); int frames = data.Length / sourceChannels;
                long start = (long)Math.Round(c.Start * rate);
                double crossfadeIn = 0, crossfadeOut = 0;
                if (job.Lane.AutoCrossfade && !c.HasRenderedEnvelope)
                    foreach (var other in job.Lane.Clips.Where(o => o.Id != c.Id))
                    {
                        double otherEnd = other.Start + snapshot.ClipDuration(other);
                        if (other.Start < c.Start && otherEnd > c.Start) crossfadeIn = Math.Max(crossfadeIn, otherEnd - c.Start);
                        if (other.Start > c.Start && other.Start < c.Start + frames / (double)rate)
                            crossfadeOut = Math.Max(crossfadeOut, c.Start + frames / (double)rate - other.Start);
                    }
                lock (gate)
                {
                    for (int f = (int)Math.Min(frames, Math.Max(0, -start)); f < frames && start + f < totalFrames; f++)
                    {
                        if ((f & 8191) == 0) ct.ThrowIfCancellationRequested();
                        double t = f / (double)rate, duration = frames / (double)rate;
                        double gain = job.Lane.Gain * c.Gain;
                        if (c.FadeIn > 0) gain *= Math.Clamp(t / c.FadeIn, 0, 1);
                        if (c.FadeOut > 0) gain *= Math.Clamp((duration - t) / c.FadeOut, 0, 1);
                        if (crossfadeIn > 0) gain *= Math.Clamp(t / crossfadeIn, 0, 1);
                        if (crossfadeOut > 0) gain *= Math.Clamp((duration - t) / crossfadeOut, 0, 1);
                        for (int ch = 0; ch < channels; ch++)
                        {
                            // Preserve stereo, duplicate mono, fold larger layouts to stereo by parity.
                            double sample = 0; int contributors = 0;
                            for (int sc = sourceChannels == 1 ? 0 : ch; sc < sourceChannels; sc += sourceChannels == 1 ? 1 : 2)
                            { sample += data[f * sourceChannels + sc]; contributors++; }
                            output[(int)(start + f) * channels + ch] += (float)(sample / Math.Max(1, contributors) * gain);
                        }
                    }
                    progress?.Report(++done / (double)Math.Max(1, jobs.Length));
                }
            });
            float peak = output.Max(v => Math.Abs(v));
            if (normalizeOutput && peak > 0.98f) for (int i = 0; i < output.Length; i++) output[i] *= 0.98f / peak;
            return new AudioObj { Name = snapshot.Name, Data = output, SampleRate = rate, Channels = channels,
                Length = output.Length, Duration = TimeSpan.FromSeconds(totalFrames / (double)rate), Bpm = (float)snapshot.Bpm, BitDepth = 32 };
        }, token);
    }
    private static float[] Resample(float[] data, int channels, double sourceRate, int rate)
    {
        if (sourceRate == rate) return (float[])data.Clone();
        int frames = data.Length / channels;
        var resampler = new NAudio.Dsp.WdlResampler();
        resampler.SetMode(true, 0, true, sinc_size: 64, sinc_interpsize: 16); resampler.SetFeedMode(false); resampler.SetRates(sourceRate, rate);
        double targetFrames = Math.Round(frames * (double)rate / sourceRate);
        if (!double.IsFinite(targetFrames) || targetFrames < 0 || targetFrames > int.MaxValue / channels)
            throw new InvalidOperationException("The resampled clip exceeds the RAM audio buffer limit.");
        var result = new float[checked((int)targetFrames * channels)];
        int sourcePosition = 0, outputPosition = 0;
        while (outputPosition < result.Length)
        {
            int requested = Math.Min(1024, (result.Length - outputPosition) / channels);
            int needed = resampler.ResamplePrepare(requested, channels, out Span<float> input);
            int supplied = Math.Min(needed, frames - sourcePosition);
            data.AsSpan(sourcePosition * channels, supplied * channels).CopyTo(input); sourcePosition += supplied;
            int written = resampler.ResampleOut(result.AsSpan(outputPosition, requested * channels), supplied, requested, channels);
            if (written == 0) break; outputPosition += written * channels;
        }
        return result;
    }

    /// <summary>
    /// Render one complete clip at the mix clock, retaining every source channel
    /// and baking clip gain/fades/crossfades, but never lane gain/mute/solo.
    /// Rendering the complete clip before cropping preserves its existing fade
    /// position and phase-vocoder context at effect-selection boundaries.
    /// </summary>
    internal static async Task<float[]> RenderClipForEffectAsync(MixArrangement arrangement, MixLane lane, MixClip clip, CancellationToken token)
    {
        const int rate = 44100;
        MixSource source = arrangement.Sources[clip.SourceId]; int channels = source.Channels;
        int begin = checked((int)Math.Min(source.Data.LongLength / channels, Math.Round(clip.SourceOffset * source.SampleRate)) * channels);
        int count = checked((int)Math.Min((source.Data.LongLength - begin) / channels, Math.Round(clip.SourceDuration * source.SampleRate)) * channels);
        float[] data = source.Data.AsSpan(begin, count).ToArray();
        double factor = arrangement.Rate(clip);
        data = Resample(data, channels, source.SampleRate * factor, rate);
        token.ThrowIfCancellationRequested();
        if (clip.TempoMode == ClipTempoMode.TimeStretch && Math.Abs(factor - 1) > 0.00001 && data.Length > 0)
        {
            using var input = new AudioObj { Data = data, Channels = channels, SampleRate = rate };
            using var shifted = await PitchShifter.CreatePitchShiftAsync(input, (float)(-12 * Math.Log2(factor)),
                useFftPv: true, maxWorkers: 1, cancellationToken: token).ConfigureAwait(false);
            data = shifted.Data;
        }
        int frames = data.Length / channels;
        double crossfadeIn = 0, crossfadeOut = 0;
        if (lane.AutoCrossfade && !clip.HasRenderedEnvelope)
            foreach (MixClip other in lane.Clips.Where(other => other.Id != clip.Id))
            {
                double otherEnd = other.Start + arrangement.ClipDuration(other);
                if (other.Start < clip.Start && otherEnd > clip.Start) crossfadeIn = Math.Max(crossfadeIn, otherEnd - clip.Start);
                if (other.Start > clip.Start && other.Start < clip.Start + frames / (double)rate)
                    crossfadeOut = Math.Max(crossfadeOut, clip.Start + frames / (double)rate - other.Start);
            }
        for (int frame = 0; frame < frames; frame++)
        {
            if ((frame & 8191) == 0) token.ThrowIfCancellationRequested();
            double time = frame / (double)rate, duration = frames / (double)rate, gain = clip.Gain;
            if (clip.FadeIn > 0) gain *= Math.Clamp(time / clip.FadeIn, 0, 1);
            if (clip.FadeOut > 0) gain *= Math.Clamp((duration - time) / clip.FadeOut, 0, 1);
            if (crossfadeIn > 0) gain *= Math.Clamp(time / crossfadeIn, 0, 1);
            if (crossfadeOut > 0) gain *= Math.Clamp((duration - time) / crossfadeOut, 0, 1);
            for (int channel = 0; channel < channels; channel++) data[frame * channels + channel] = (float)(data[frame * channels + channel] * gain);
        }
        return data;
    }
}
