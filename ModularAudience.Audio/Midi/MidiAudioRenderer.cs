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
    public static AudioObj Render(MidiFileData midi, int trackIndex, MidiInstrument instrument, double bpm, AudioObj? customSample = null, int sampleRate = 44100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(midi);
        bpm = Math.Clamp(bpm, 20.0, 400.0);
        MidiTrackData track = midi.Tracks.FirstOrDefault(candidate => candidate.Index == trackIndex) ?? throw new ArgumentOutOfRangeException(nameof(trackIndex));
        int channels = 2;
        double secondsPerTick = 60.0 / bpm / Math.Max(1, midi.TicksPerQuarterNote);
        double durationSeconds = Math.Max(0.1, (track.LengthTicks + midi.TicksPerQuarterNote / 2.0) * secondsPerTick);
        int frameCount = checked((int)Math.Min(int.MaxValue / channels, Math.Ceiling(durationSeconds * sampleRate)));
        float[] output = new float[frameCount * channels];
        Random random = new(17);

        foreach (MidiNoteData note in track.Notes.OrderBy(note => note.StartTick))
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
            double frequency = 440.0 * Math.Pow(2.0, (note.NoteNumber - 69) / 12.0);
            MixNote(output, frameCount, channels, sampleRate, startFrame, noteFrames, frequency, amplitude, instrument, customSample, random, cancellationToken);
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

    private static void MixNote(float[] output, int frameCount, int channels, int sampleRate, int startFrame, int noteFrames, double frequency, float amplitude, MidiInstrument instrument, AudioObj? customSample, Random random, CancellationToken cancellationToken)
    {
        if (startFrame >= frameCount)
        {
            return;
        }

        int availableFrames = Math.Min(noteFrames, frameCount - Math.Max(0, startFrame));
        if (instrument == MidiInstrument.CustomSample && customSample?.Data is { Length: > 0 } && customSample.SampleRate > 0)
        {
            MixSample(output, frameCount, channels, sampleRate, startFrame, availableFrames, frequency, amplitude, customSample, cancellationToken);
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
    private static void MixSample(float[] output, int frameCount, int channels, int sampleRate, int startFrame, int noteFrames, double frequency, float amplitude, AudioObj sample, CancellationToken cancellationToken)
    {
        int sourceChannels = Math.Max(1, sample.Channels);
        int sourceFrames = sample.Data.Length / sourceChannels;
        if (sourceFrames <= 0 || noteFrames <= 0)
        {
            return;
        }

        double sourceStep = frequency / 261.625565 * sample.SampleRate / sampleRate;
        int grainSize = Math.Clamp(sampleRate / 20, 512, 2048);
        int synthesisHop = grainSize / 4;
        float[] accumulated = new float[noteFrames * channels];
        float[] weights = new float[noteFrames];

        for (int grainStart = 0; grainStart < noteFrames; grainStart += synthesisHop)
        {
            if ((grainStart & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            double sourceStart = Math.Min(grainStart * sourceStep, sourceFrames - 1);
            int grainFrames = Math.Min(grainSize, noteFrames - grainStart);
            for (int grainFrame = 0; grainFrame < grainFrames; grainFrame++)
            {
                int targetFrame = grainStart + grainFrame;
                double sourcePosition = Math.Min(sourceFrames - 1, sourceStart + grainFrame * sourceStep);
                int sourceFrame = (int)sourcePosition;
                double fraction = sourcePosition - sourceFrame;
                int nextFrame = Math.Min(sourceFrames - 1, sourceFrame + 1);
                double phase = grainFrame / (double)Math.Max(1, grainSize - 1);
                float window = (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * phase));
                weights[targetFrame] += window;

                for (int channel = 0; channel < channels; channel++)
                {
                    int sourceChannel = Math.Min(sourceChannels - 1, channel);
                    float a = sample.Data[sourceFrame * sourceChannels + sourceChannel];
                    float b = sample.Data[nextFrame * sourceChannels + sourceChannel];
                    accumulated[targetFrame * channels + channel] += (float)(a + (b - a) * fraction) * window;
                }
            }
        }

        float peak = 0f;
        for (int frame = 0; frame < noteFrames; frame++)
        {
            float weight = Math.Max(0.001f, weights[frame]);
            for (int channel = 0; channel < channels; channel++)
            {
                peak = Math.Max(peak, Math.Abs(accumulated[frame * channels + channel] / weight));
            }
        }

        float normalizationGain = peak > 1e-6f ? 1f / peak : 1f;
        for (int frame = 0; frame < noteFrames; frame++)
        {
            int outputFrame = startFrame + frame;
            if (outputFrame < 0 || outputFrame >= frameCount)
            {
                continue;
            }

            float weight = Math.Max(0.001f, weights[frame]);
            int outputIndex = outputFrame * channels;
            for (int channel = 0; channel < channels; channel++)
            {
                float value = accumulated[frame * channels + channel] / weight * normalizationGain * amplitude;
                output[outputIndex + channel] = Math.Clamp(output[outputIndex + channel] + value, -1f, 1f);
            }
        }
    }
}
