using ModularAudience.Audio.Processors_V1;

namespace ModularAudience.Audio.Midi;

public enum MidiInstrument
{
    Sine,
    Saw,
    Square,
    Triangle,
    Noise,
    Pluck,
    CustomSample
}

public static class MidiAudioRenderer
{
    public static AudioObj Render(MidiFileData midi, int trackIndex, MidiInstrument instrument, double bpm, AudioObj? customSample = null, int sampleRate = 44100, CancellationToken cancellationToken = default, double pitchFrequency = 440.0, bool previewQuality = false, IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(midi);
        bpm = Math.Clamp(bpm, 20.0, 400.0);
        pitchFrequency = Math.Clamp(pitchFrequency, 1.0, 1000.0);
        if (previewQuality)
        {
            sampleRate = Math.Min(sampleRate, 22050);
        }
        MidiTrackData track = midi.Tracks.FirstOrDefault(candidate => candidate.Index == trackIndex) ?? throw new ArgumentOutOfRangeException(nameof(trackIndex));
        int channels = 2;
        double secondsPerTick = 60.0 / bpm / Math.Max(1, midi.TicksPerQuarterNote);
        double durationSeconds = Math.Max(0.1, (track.LengthTicks + midi.TicksPerQuarterNote / 2.0) * secondsPerTick);
        int frameCount = checked((int)Math.Min(int.MaxValue / channels, Math.Ceiling(durationSeconds * sampleRate)));
        float[] output = new float[frameCount * channels];
        Random random = new(17);
        List<MidiNoteData> orderedNotes = track.Notes.OrderBy(note => note.StartTick).ToList();
        Dictionary<(int NoteNumber, int FrameCount), AudioObj>? previewSamples = previewQuality && instrument == MidiInstrument.CustomSample
            ? []
            : null;
        int noteIndex = 0;

        try
        {
            foreach (MidiNoteData note in orderedNotes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double startSeconds = note.StartTick * secondsPerTick;
                double noteSeconds = Math.Max(0.01, note.DurationTicks * secondsPerTick);
                int startFrame = (int)Math.Round(startSeconds * sampleRate);
                int noteFrames = Math.Max(1, (int)Math.Round(noteSeconds * sampleRate));
                float instrumentGain = instrument switch
                {
                    MidiInstrument.Sine => 1.25f,
                    MidiInstrument.CustomSample => 0.8f,
                    _ => 0.22f
                };
                float amplitude = Math.Clamp(note.Velocity / 127f, 0.05f, 1f) * instrumentGain;
                double frequency = pitchFrequency * Math.Pow(2.0, (note.NoteNumber - 69) / 12.0);
                AudioObj? preparedCustomSample = null;
                if (instrument == MidiInstrument.CustomSample && customSample?.Data is { Length: > 0 } && customSample.SampleRate > 0)
                {
                    if (previewSamples == null || !previewSamples.TryGetValue((note.NoteNumber, noteFrames), out preparedCustomSample))
                    {
                        preparedCustomSample = PrepareCustomSample(customSample, noteFrames, frequency, pitchFrequency, cancellationToken, previewQuality);
                        previewSamples?.Add((note.NoteNumber, noteFrames), preparedCustomSample);
                    }
                }

                try
                {
                    MixNote(output, frameCount, channels, sampleRate, startFrame, noteFrames, frequency, amplitude, instrument, preparedCustomSample ?? customSample, random, cancellationToken);
                }
                finally
                {
                    if (previewSamples == null && preparedCustomSample != null && !ReferenceEquals(preparedCustomSample, customSample))
                    {
                        preparedCustomSample.Dispose();
                    }
                }

                progress?.Report(++noteIndex / (double) Math.Max(1, orderedNotes.Count));
            }
        }
        finally
        {
            if (previewSamples != null)
            {
                foreach (AudioObj preparedSample in previewSamples.Values)
                {
                    preparedSample.Dispose();
                }
            }
        }

        if (instrument == MidiInstrument.Sine)
        {
            NormalizePeak(output, 0.95f);
            LogSineRender(track, output, sampleRate);
        }

        AudioObj rendered = new()
        {
            Name = $"{Path.GetFileNameWithoutExtension(midi.FilePath)} - {track.Name}",
            FilePath = string.Empty,
            Data = output,
            SampleRate = sampleRate,
            Channels = channels,
            BitDepth = 32,
            Length = output.Length,
            Duration = TimeSpan.FromSeconds(frameCount / (double)sampleRate),
            Bpm = (float)bpm,
            Volume = 100f
        };
        rendered.Rename(rendered.Name);
        return rendered;
    }

    private static AudioObj PrepareCustomSample(AudioObj sample, int targetFrames, double targetFrequency, double referenceFrequency, CancellationToken cancellationToken, bool previewQuality)
    {
        double sampleRootFrequency = referenceFrequency * Math.Pow(2.0, (60 - 69) / 12.0);
        double semitones = 12.0 * Math.Log2(Math.Max(1.0, targetFrequency) / Math.Max(1.0, sampleRootFrequency));
        if (previewQuality)
        {
            return PreparePreviewCustomSample(sample, targetFrames, semitones, cancellationToken);
        }

        AudioObj pitchedSample = Math.Abs(semitones) < 0.001
            ? sample.Clone()
            : PitchShifter.CreatePitchShiftWithoutTimestretchAsync(sample, (float) semitones)
                .GetAwaiter()
                .GetResult();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            int sourceFrames = pitchedSample.Data.Length / Math.Max(1, pitchedSample.Channels);
            if (sourceFrames > 0 && sourceFrames != targetFrames)
            {
                double stretchFactor = targetFrames / (double) sourceFrames;
                TimeStretcher.TimeStretchAllThreadsAsync(
                    pitchedSample,
                    chunkSize: previewQuality ? 2048 : 16384,
                    overlap: previewQuality ? 0.25f : 0.5f,
                    factor: stretchFactor,
                    normalize: 0.0f,
                    maxWorkers: Environment.ProcessorCount,
                    progress: null,
                    offload: false,
                    channeled: false).GetAwaiter().GetResult();
            }

            return pitchedSample;
        }
        catch
        {
            pitchedSample.Dispose();
            throw;
        }
    }

    private static AudioObj PreparePreviewCustomSample(AudioObj sample, int targetFrames, double semitones, CancellationToken cancellationToken)
    {
        int channels = Math.Max(1, sample.Channels);
        int sourceFrames = sample.Data.Length / channels;
        if (sourceFrames <= 0)
        {
            return sample.Clone();
        }

        double pitchRatio = Math.Pow(2.0, semitones / 12.0);
        int pitchedFrames = Math.Max(1, (int) Math.Round(sourceFrames / pitchRatio));
        float[] pitchedData = new float[pitchedFrames * channels];
        for (int frame = 0; frame < pitchedFrames; frame++)
        {
            double sourcePosition = Math.Min(sourceFrames - 1, frame * pitchRatio);
            int sourceFrame = (int) sourcePosition;
            int nextFrame = Math.Min(sourceFrames - 1, sourceFrame + 1);
            float fraction = (float) (sourcePosition - sourceFrame);
            for (int channel = 0; channel < channels; channel++)
            {
                float first = sample.Data[sourceFrame * channels + channel];
                float second = sample.Data[nextFrame * channels + channel];
                pitchedData[frame * channels + channel] = first + (second - first) * fraction;
            }
        }

        float[] data = new float[Math.Max(1, targetFrames) * channels];
        float[] weights = new float[data.Length / channels];
        int grainSize = Math.Min(1024, Math.Max(64, pitchedFrames));
        int synthesisHop = Math.Max(1, grainSize / 4);
        int grainCount = Math.Max(1, (int) Math.Ceiling(targetFrames / (double) synthesisHop));
        for (int grain = 0; grain < grainCount; grain++)
        {
            if ((grain & 31) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int outputStart = grain * synthesisHop;
            int sourceStart = Math.Min(Math.Max(0, pitchedFrames - grainSize), (int) Math.Round(grain * Math.Max(0, pitchedFrames - grainSize) / (double) Math.Max(1, grainCount - 1)));
            for (int offset = 0; offset < grainSize && outputStart + offset < targetFrames; offset++)
            {
                int sourceFrame = Math.Min(pitchedFrames - 1, sourceStart + offset);
                float window = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * offset / Math.Max(1, grainSize - 1));
                int outputFrame = outputStart + offset;
                weights[outputFrame] += window;
                for (int channel = 0; channel < channels; channel++)
                {
                    data[outputFrame * channels + channel] += pitchedData[sourceFrame * channels + channel] * window;
                }
            }
        }

        for (int frame = 0; frame < targetFrames; frame++)
        {
            float weight = weights[frame];
            if (weight <= 1e-6f)
            {
                continue;
            }

            for (int channel = 0; channel < channels; channel++)
            {
                data[frame * channels + channel] /= weight;
            }
        }

        float sourcePeak = FindPeak(sample.Data);
        NormalizeSamplePeak(data, sourcePeak);

        return new AudioObj
        {
            Name = sample.Name,
            SampleRate = sample.SampleRate,
            Channels = channels,
            BitDepth = sample.BitDepth,
            Data = data,
            Length = data.LongLength,
            Duration = TimeSpan.FromSeconds(targetFrames / (double) Math.Max(1, sample.SampleRate)),
            Volume = sample.Volume
        };
    }

    private static float FindPeak(float[] data)
    {
        float peak = 0f;
        foreach (float value in data)
        {
            peak = Math.Max(peak, Math.Abs(value));
        }

        return peak;
    }

    private static void NormalizeSamplePeak(float[] data, float targetPeak)
    {
        if (targetPeak <= 1e-6f)
        {
            return;
        }

        float peak = FindPeak(data);
        if (peak <= 1e-6f)
        {
            return;
        }

        float gain = targetPeak / peak;
        for (int index = 0; index < data.Length; index++)
        {
            data[index] = Math.Clamp(data[index] * gain, -1f, 1f);
        }
    }

    private static void MixNote(float[] output, int frameCount, int channels, int sampleRate, int startFrame, int noteFrames, double frequency, float amplitude, MidiInstrument instrument, AudioObj? customSample, Random random, CancellationToken cancellationToken)
    {
        if (startFrame >= frameCount)
        {
            return;
        }

        int availableFrames = Math.Min(noteFrames, frameCount - Math.Max(0, startFrame));
        if (instrument == MidiInstrument.CustomSample && customSample?.Data is { Length: > 0 } && customSample.SampleRate > 0)
        {
            MixSample(output, frameCount, channels, sampleRate, startFrame, availableFrames, amplitude, customSample, cancellationToken);
            return;
        }

        double phase = instrument == MidiInstrument.Sine ? Math.PI / 2.0 : 0.0;
        double phaseStep = frequency / sampleRate;
        float[]? pluckBuffer = instrument == MidiInstrument.Pluck
            ? CreatePluckBuffer(frequency, random)
            : null;
        int pluckIndex = 0;
        for (int frame = 0; frame < availableFrames; frame++)
        {
            if ((frame & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            double attack = Math.Min(1.0, frame / Math.Max(1.0, sampleRate * (instrument == MidiInstrument.Sine ? 0.002 : 0.005)));
            double release = Math.Min(1.0, (availableFrames - frame) / Math.Max(1.0, sampleRate * (instrument == MidiInstrument.Sine ? 0.015 : 0.03)));
            double envelope = Math.Min(attack, release);
            double value = instrument switch
            {
                MidiInstrument.Sine => RenderAudibleSine(phase, frequency),
                MidiInstrument.Saw => 2.0 * (phase - Math.Floor(phase + 0.5)),
                MidiInstrument.Square => phase < 0.5 ? 1.0 : -1.0,
                MidiInstrument.Triangle => 1.0 - 4.0 * Math.Abs(Math.Round(phase) - phase),
                MidiInstrument.Noise => random.NextDouble() * 2.0 - 1.0,
                MidiInstrument.Pluck => RenderPluckSample(pluckBuffer!, ref pluckIndex),
                _ => 0.0
            };
            float sample = (float)(value * envelope * amplitude);
            int outputIndex = (Math.Max(0, startFrame) + frame) * channels;
            for (int channel = 0; channel < channels; channel++)
            {
                output[outputIndex + channel] = Math.Clamp(output[outputIndex + channel] + sample, -1f, 1f);
            }
            phase = instrument == MidiInstrument.Sine
                ? (phase + 2.0 * Math.PI * phaseStep) % (2.0 * Math.PI)
                : (phase + phaseStep) % 1.0;
        }
    }

    private static double RenderAudibleSine(double phase, double frequency)
    {
        double fundamental = Math.Sin(phase);
        if (frequency >= 260.0)
        {
            return fundamental;
        }

        double harmonicBlend = Math.Clamp((260.0 - frequency) / 260.0, 0.0, 1.0);
        double secondHarmonic = Math.Sin(phase * 2.0) * 0.48 * harmonicBlend;
        double thirdHarmonic = Math.Sin(phase * 3.0) * 0.24 * harmonicBlend;
        double fourthHarmonic = Math.Sin(phase * 4.0) * 0.10 * harmonicBlend;
        return fundamental + secondHarmonic + thirdHarmonic + fourthHarmonic;
    }

    private static void LogSineRender(MidiTrackData track, float[] output, int sampleRate)
    {
        float peak = 0f;
        double sumSquares = 0;
        for (int index = 0; index < output.Length; index++)
        {
            float sample = output[index];
            peak = Math.Max(peak, Math.Abs(sample));
            sumSquares += sample * sample;
        }

        MidiNoteData? firstNote = track.Notes.OrderBy(note => note.StartTick).FirstOrDefault();
        if (firstNote != null)
        {
            double frequency = 440.0 * Math.Pow(2.0, (firstNote.NoteNumber - 69) / 12.0);
            double rms = Math.Sqrt(sumSquares / Math.Max(1, output.Length));
            LogCollection.Log($"MIDI sine rendered: note={firstNote.NoteNumber}, frequency={frequency:F2}Hz, peak={peak:F3}, rms={rms:F3}, samples={output.Length}, sampleRate={sampleRate}, durationTicks={firstNote.DurationTicks}");
        }
    }

    private static void NormalizePeak(float[] output, float targetPeak)
    {
        float peak = 0f;
        for (int index = 0; index < output.Length; index++)
        {
            peak = Math.Max(peak, Math.Abs(output[index]));
        }

        if (peak <= 1e-6f)
        {
            return;
        }

        float gain = targetPeak / peak;
        for (int index = 0; index < output.Length; index++)
        {
            output[index] = Math.Clamp(output[index] * gain, -targetPeak, targetPeak);
        }
    }

    private static float[] CreatePluckBuffer(double frequency, Random random)
    {
        int length = Math.Clamp((int)Math.Round(44100.0 / Math.Clamp(frequency, 40.0, 4000.0)), 2, 2205);
        float[] buffer = new float[length];
        for (int index = 0; index < buffer.Length; index++)
        {
            buffer[index] = (float)(random.NextDouble() * 2.0 - 1.0);
        }
        return buffer;
    }

    private static float RenderPluckSample(float[] buffer, ref int index)
    {
        float current = buffer[index];
        int next = (index + 1) % buffer.Length;
        buffer[index] = 0.996f * 0.5f * (buffer[index] + buffer[next]);
        index = next;
        return current;
    }
    private static void MixSample(float[] output, int frameCount, int channels, int sampleRate, int startFrame, int noteFrames, float amplitude, AudioObj sample, CancellationToken cancellationToken)
    {
        int sourceChannels = Math.Max(1, sample.Channels);
        int sourceFrames = sample.Data.Length / sourceChannels;
        if (sourceFrames <= 0 || noteFrames <= 0)
        {
            return;
        }

        for (int frame = 0; frame < noteFrames; frame++)
        {
            if ((frame & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int outputFrame = startFrame + frame;
            if (outputFrame < 0 || outputFrame >= frameCount)
            {
                continue;
            }

            double sourcePosition = frame / (double) Math.Max(1, noteFrames - 1) * Math.Max(0, sourceFrames - 1);
            int sourceFrame = Math.Min(sourceFrames - 1, (int) sourcePosition);
            int nextFrame = Math.Min(sourceFrames - 1, sourceFrame + 1);
            double fraction = sourcePosition - sourceFrame;
            double envelope = Math.Min(
                1.0,
                Math.Min(
                    (frame + 1) / Math.Max(1.0, sampleRate * 0.005),
                    (noteFrames - frame) / Math.Max(1.0, sampleRate * 0.03)));
            int outputIndex = outputFrame * channels;
            for (int channel = 0; channel < channels; channel++)
            {
                int sourceChannel = Math.Min(sourceChannels - 1, channel);
                float a = sample.Data[sourceFrame * sourceChannels + sourceChannel];
                float b = sample.Data[nextFrame * sourceChannels + sourceChannel];
                float value = (float) ((a + (b - a) * fraction) * envelope * amplitude);
                output[outputIndex + channel] = Math.Clamp(output[outputIndex + channel] + value, -1f, 1f);
            }
        }
    }
}
