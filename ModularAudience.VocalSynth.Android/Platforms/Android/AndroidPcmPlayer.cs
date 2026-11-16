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
            throw new ArgumentOutOfRangeException(nameof(startFrame));
        int startSample = (int)startSamplePosition;
        Stop();
        cancellationToken.ThrowIfCancellationRequested();

        ChannelOut channelMask = audio.Channels == 1 ? ChannelOut.Mono : ChannelOut.Stereo;
        int bufferSize = Math.Max(
            AudioTrack.GetMinBufferSize(audio.SampleRate, channelMask, AudioEncoding.Pcm16bit),
            audio.SampleRate * audio.Channels / 2);
        AudioFormat format = new AudioFormat.Builder()
            .SetEncoding(AudioEncoding.Pcm16bit)
            .SetSampleRate(audio.SampleRate)
            .SetChannelMask(channelMask)
            .Build();
        AudioAttributes attributes = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Media)
            .SetContentType(AudioContentType.Speech)
            .Build();
        AudioTrack output = new(attributes, format, bufferSize, AudioTrackMode.Stream, 0);
        CancellationTokenSource playback = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (gate)
        {
            track = output;
            playbackCancellation = playback;
        }

        short[] chunk = new short[8192];
        try
        {
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
                        throw new InvalidOperationException($"Android audio output failed with code {written}.");
                    position += written;
                }
            }, playback.Token);

            long frameCount = (audio.Data.LongLength - startSample) / audio.Channels;
            TimeSpan drainTimeout = TimeSpan.FromSeconds(frameCount / (double)audio.SampleRate)
                + TimeSpan.FromSeconds(2);
            Stopwatch drainTimer = Stopwatch.StartNew();
            while (output.PlaybackHeadPosition < frameCount)
            {
                playback.Token.ThrowIfCancellationRequested();
                if (drainTimer.Elapsed > drainTimeout)
                    throw new InvalidOperationException("Android audio playback did not reach the end of the audio.");
                await Task.Delay(10, playback.Token);
            }
        }
        finally
        {
            lock (gate)
            {
                if (ReferenceEquals(track, output)) track = null;
                if (ReferenceEquals(playbackCancellation, playback)) playbackCancellation = null;
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
