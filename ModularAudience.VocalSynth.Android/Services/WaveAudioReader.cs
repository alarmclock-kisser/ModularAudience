using System.Buffers.Binary;

namespace ModularAudience.VocalSynth.Android.Services;

internal sealed record WaveAudioData(float[] Samples, int SampleRate, int Channels);

internal static class WaveAudioReader
{
    internal static WaveAudioData Read(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using MemoryStream buffer = new();
        source.CopyTo(buffer);
        ReadOnlySpan<byte> bytes = buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length));
        if (bytes.Length < 12 || !bytes[..4].SequenceEqual("RIFF"u8)
            || !bytes[8..12].SequenceEqual("WAVE"u8))
            throw new InvalidDataException("The selected file is not a valid WAV file.");

        ushort format = 0;
        ushort channels = 0;
        int sampleRate = 0;
        ushort bitsPerSample = 0;
        ReadOnlySpan<byte> data = [];
        for (int offset = 12; offset <= bytes.Length - 8;)
        {
            ReadOnlySpan<byte> id = bytes.Slice(offset, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 4, 4));
            if (size > int.MaxValue || size > bytes.Length - offset - 8)
                throw new InvalidDataException("The WAV file contains an invalid chunk size.");
            ReadOnlySpan<byte> chunk = bytes.Slice(offset + 8, (int)size);
            if (id.SequenceEqual("fmt "u8))
            {
                if (chunk.Length < 16) throw new InvalidDataException("The WAV format chunk is incomplete.");
                format = BinaryPrimitives.ReadUInt16LittleEndian(chunk);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]);
                sampleRate = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]));
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]);
                if (format == 0xfffe && chunk.Length >= 40)
                    format = BinaryPrimitives.ReadUInt16LittleEndian(chunk[24..]);
            }
            else if (id.SequenceEqual("data"u8))
            {
                data = chunk;
            }
            offset = checked(offset + 8 + (int)size + ((int)size & 1));
        }

        if (channels is < 1 or > 2 || sampleRate <= 0 || data.IsEmpty)
            throw new InvalidDataException("The WAV file has no supported mono or stereo audio data.");
        int bytesPerSample = bitsPerSample / 8;
        if (bitsPerSample % 8 != 0 || bytesPerSample is < 1 or > 4
            || data.Length % (bytesPerSample * channels) != 0)
            throw new InvalidDataException("The WAV sample format is not supported.");
        if (format != 1 && format != 3)
            throw new InvalidDataException("Only PCM and IEEE float WAV files are supported.");
        if (format == 3 && bitsPerSample != 32 || format == 1 && bitsPerSample is not (8 or 16 or 24 or 32))
            throw new InvalidDataException("The WAV sample bit depth is not supported.");

        float[] samples = new float[data.Length / bytesPerSample];
        for (int index = 0; index < samples.Length; index++)
        {
            ReadOnlySpan<byte> sample = data.Slice(index * bytesPerSample, bytesPerSample);
            float value = format == 3
                ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(sample))
                : bitsPerSample switch
                {
                    8 => (sample[0] - 128) / 128f,
                    16 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768f,
                    24 => ReadInt24(sample) / 8388608f,
                    32 => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648f,
                    _ => throw new InvalidDataException("The WAV sample bit depth is not supported."),
                };
            if (!float.IsFinite(value)) throw new InvalidDataException("The WAV contains an invalid sample.");
            samples[index] = Math.Clamp(value, -1, 1);
        }
        return new WaveAudioData(samples, sampleRate, channels);
    }

    private static int ReadInt24(ReadOnlySpan<byte> sample)
    {
        int value = sample[0] | sample[1] << 8 | sample[2] << 16;
        return (value & 0x800000) == 0 ? value : value | unchecked((int)0xff000000);
    }
}
