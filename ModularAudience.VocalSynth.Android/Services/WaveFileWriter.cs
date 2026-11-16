using System.Buffers.Binary;
using ModularAudience.Audio.Vocal;

namespace ModularAudience.VocalSynth.Android.Services;

internal static class WaveFileWriter
{
    internal static async Task WritePcm16Async(
        string filePath, VocalAudioData audio, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(audio);
        if (audio.Channels is < 1 or > 2 || audio.SampleRate <= 0 || audio.Data.Length % audio.Channels != 0)
            throw new InvalidDataException("The generated audio format is not supported for WAV export.");

        await using FileStream stream = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        byte[] header = new byte[44];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), checked(audio.Data.Length * 2 + 36));
        "WAVEfmt "u8.CopyTo(header.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(22), checked((short)audio.Channels));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), audio.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28), checked(audio.SampleRate * audio.Channels * 2));
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(32), checked((short)(audio.Channels * 2)));
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(34), 16);
        "data"u8.CopyTo(header.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(40), checked(audio.Data.Length * 2));
        await stream.WriteAsync(header, cancellationToken);

        byte[] samples = new byte[8192];
        for (int offset = 0; offset < audio.Data.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(samples.Length / 2, audio.Data.Length - offset);
            for (int index = 0; index < count; index++)
            {
                float value = Math.Clamp(audio.Data[offset + index], -1, 1);
                short pcm = (short)Math.Round(value * (value < 0 ? 32768 : 32767));
                BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(index * 2), pcm);
            }
            await stream.WriteAsync(samples.AsMemory(0, count * 2), cancellationToken);
            offset += count;
        }

        await stream.FlushAsync(cancellationToken);
        long expectedLength = 44L + audio.Data.LongLength * sizeof(short);
        if (stream.Length != expectedLength)
            throw new IOException("The exported WAV file does not contain all generated audio samples.");
    }
}
