using System.Diagnostics;
using Android.Media;
using ModularAudience.Audio.Vocal;
using AudioEncoding = Android.Media.Encoding;

namespace ModularAudience.VocalSynth.Android;

internal sealed class AndroidPcmPlayer : IDisposable
{
    private readonly object gate = new();
    private AudioTrack? track;
    private CancellationTokenSource? playbackCancellation;

    internal async Task PlayAsync(
        VocalAudioData audio, CancellationToken cancellationToken, int startFrame = 0)
    {
        ArgumentNullException.ThrowIfNull(audio);
        long startSamplePosition = (long)startFrame * audio.Channels;
        if (startFrame < 0 || startSamplePosition >= audio.Data.LongLength)
        {
            throw new ArgumentOutOfRangeException(nameof(startFrame));
        }

        int startSample = (int)startSamplePosition;
        Stop();
        cancellationToken.ThrowIfCancellationRequested();

        if (audio.SampleRate <= 0 || audio.Channels is < 1 or > 2 ||
            audio.Data.Length % audio.Channels != 0)
        {
            throw new InvalidDataException("The audio has an unsupported sample rate, channel count, or frame length.");
        }

        ChannelOut channelMask = audio.Channels == 1 ? ChannelOut.Mono : ChannelOut.Stereo;
        int minimumBufferSize = AudioTrack.GetMinBufferSize(
            audio.SampleRate,
            channelMask,
            AudioEncoding.Pcm16bit);
        if (minimumBufferSize <= 0)
        {
            throw new InvalidOperationException(
                $"Android does not support PCM playback at {audio.SampleRate} Hz with {audio.Channels} channel(s).");
        }
        long requestedBufferSize = Math.Max(minimumBufferSize, (long)audio.SampleRate * audio.Channels / 2);
        if (requestedBufferSize > int.MaxValue)
        {
            throw new InvalidOperationException("The requested Android audio buffer is too large.");
        }

        int bufferSize = (int)requestedBufferSize;
        AudioFormat format = new AudioFormat.Builder()
            .SetEncoding(AudioEncoding.Pcm16bit)
            .SetSampleRate(audio.SampleRate)
            .SetChannelMask(channelMask)
            .Build();
        AudioAttributes attributes = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Media)
            .SetContentType(AudioContentType.Speech)
            .Build();
        AudioTrack output;
        try
        {
            output = new AudioTrack(attributes, format, bufferSize, AudioTrackMode.Stream, 0);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Android could not initialize audio output at {audio.SampleRate} Hz with {audio.Channels} channel(s).",
                ex);
        }
        if (output.State != AudioTrackState.Initialized)
        {
            output.Release();
            throw new InvalidOperationException(
                $"Android could not initialize audio output at {audio.SampleRate} Hz with {audio.Channels} channel(s).");
        }
        CancellationTokenSource playback = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (gate)
        {
            track = output;
            playbackCancellation = playback;
        }

        short[] chunk = new short[8192];
        try
        {
            Stopwatch playbackTimer = Stopwatch.StartNew();
            output.Play();
            await Task.Run(() =>
            {
                int position = startSample;
                while (position < audio.Data.Length)
                {
                    playback.Token.ThrowIfCancellationRequested();
                    int count = Math.Min(chunk.Length, audio.Data.Length - position);
                    for (int index = 0; index < count; index++)
                    {
                        float sample = Math.Clamp(audio.Data[position + index], -1, 1);
                        chunk[index] = (short)Math.Round(sample * (sample < 0 ? 32768 : 32767));
                    }
                    int written = output.Write(chunk, 0, count, WriteMode.Blocking);
                    if (written < 0)
                    {
                        throw new InvalidOperationException($"Android audio output failed with code {written}.");
                    }

                    if (written == 0)
                    {
                        throw new InvalidOperationException("Android audio output stopped accepting audio data.");
                    }

                    position += written;
                }
            }, playback.Token);

            long frameCount = (audio.Data.LongLength - startSample) / audio.Channels;
            long bufferFrames = bufferSize / (audio.Channels * sizeof(short));
            TimeSpan bufferDrainMargin = TimeSpan.FromSeconds(
                Math.Max(0.25, bufferFrames / (double)audio.SampleRate));
            TimeSpan expectedDrainTime = TimeSpan.FromSeconds(frameCount / (double)audio.SampleRate)
                + bufferDrainMargin;
            while (output.PlaybackHeadPosition < frameCount && playbackTimer.Elapsed < expectedDrainTime)
            {
                playback.Token.ThrowIfCancellationRequested();
                await Task.Delay(10, playback.Token);
            }
            playback.Token.ThrowIfCancellationRequested();
            if (output.PlaybackHeadPosition < frameCount)
            {
                Trace.TraceWarning(
                    $"Android audio playback head reported {output.PlaybackHeadPosition} of {frameCount} frames; " +
                    "the expected playback duration elapsed, so playback completed using the duration fallback.");
            }
        }
        catch (OperationCanceledException) when (playback.IsCancellationRequested)
        {
        }
        finally
        {
            lock (gate)
            {
                if (ReferenceEquals(track, output))
                {
                    track = null;
                }

                if (ReferenceEquals(playbackCancellation, playback))
                {
                    playbackCancellation = null;
                }
            }
            playback.Dispose();
            output.Release();
        }
    }

    internal void Stop()
    {
        lock (gate)
        {
            playbackCancellation?.Cancel();
            if (track is not null)
            {
                track.Stop();
                track = null;
            }
        }
    }

    public void Dispose() => Stop();
}
