using Android.Media;
using ModularAudience.Audio.Vocal;
using AudioEncoding = Android.Media.Encoding;

namespace ModularAudience.VocalSynth.Android;

internal sealed class AndroidPcmPlayer : IDisposable
{
    private readonly object gate = new();
    private AudioTrack? track;
    private CancellationTokenSource? playbackCancellation;

    internal async Task PlayAsync(VocalAudioData audio, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
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
                int position = 0;
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
