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
            float amplitude = Math.Clamp(note.Velocity / 127f, 0.05f, 1f) * 0.22f;
            double frequency = 440.0 * Math.Pow(2.0, (note.NoteNumber - 69) / 12.0);
            MixNote(output, frameCount, channels, sampleRate, startFrame, noteFrames, frequency, amplitude, instrument, customSample, random, cancellationToken);
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
            Bpm = (float)bpm
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

        double phase = 0.0;
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

            double progress = frame / (double)Math.Max(1, availableFrames);
            double attack = Math.Min(1.0, progress / 0.015);
            double release = Math.Min(1.0, (1.0 - progress) / 0.12);
            double envelope = Math.Min(attack, release);
            double value = instrument switch
            {
                MidiInstrument.Saw => 2.0 * (phase - Math.Floor(phase + 0.5)),
                MidiInstrument.Square => phase < 0.5 ? 1.0 : -1.0,
                MidiInstrument.Triangle => 1.0 - 4.0 * Math.Abs(Math.Round(phase) - phase),
                MidiInstrument.Noise => random.NextDouble() * 2.0 - 1.0,
                MidiInstrument.Pluck => RenderPluckSample(pluckBuffer!, ref pluckIndex),
                _ => Math.Sin(phase * 2.0 * Math.PI)
            };
            float sample = (float)(value * envelope * amplitude);
            int outputIndex = (Math.Max(0, startFrame) + frame) * channels;
            for (int channel = 0; channel < channels; channel++)
            {
                output[outputIndex + channel] = Math.Clamp(output[outputIndex + channel] + sample, -1f, 1f);
            }
            phase = (phase + phaseStep) % 1.0;
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
        double sourceStep = frequency / 261.625565 * sample.SampleRate / sampleRate;
        for (int frame = 0; frame < noteFrames; frame++)
        {
            if ((frame & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            double sourcePosition = frame * sourceStep;
            if (sourcePosition >= sourceFrames)
            {
                break;
            }
            int sourceFrame = (int)sourcePosition;
            double fraction = sourcePosition - sourceFrame;
            int nextFrame = Math.Min(sourceFrames - 1, sourceFrame + 1);
            int outputIndex = (startFrame + frame) * channels;
            for (int channel = 0; channel < channels; channel++)
            {
                int sourceChannel = Math.Min(sourceChannels - 1, channel);
                float a = sample.Data[sourceFrame * sourceChannels + sourceChannel];
                float b = sample.Data[nextFrame * sourceChannels + sourceChannel];
                float value = (float)(a + (b - a) * fraction) * amplitude;
                output[outputIndex + channel] = Math.Clamp(output[outputIndex + channel] + value, -1f, 1f);
            }
        }
    }
}
