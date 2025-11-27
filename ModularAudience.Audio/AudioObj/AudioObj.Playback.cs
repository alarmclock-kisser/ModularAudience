using NAudio.Wave;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ModularAudience.Audio
{
    public partial class AudioObj
    {
        public bool PlayerPlaying => this.Playing && !this.Paused;

        public long CurrentPlaybackPositionBytes
        {
            get
            {
                if (this.PlayerPlaying)
                {
                    long gp = 0;
                    try { gp = this.playback.GetPositionBytes(); }
                    catch { gp = 0; }

                    long delta = gp - this.positionOriginBytes;
                    if (delta < 0) { delta = 0; }
                    long absolute = this.SkippedPositionBytes + delta;

                    if (this.playbackLoopApplied && this.playbackLoopLengthBytes > 0)
                    {
                        if (absolute < this.playbackLoopStartBytes)
                        {
                            return absolute;
                        }

                        long rel = (absolute - this.playbackLoopStartBytes) % this.playbackLoopLengthBytes;
                        return this.playbackLoopStartBytes + rel;
                    }

                    return absolute;
                }

                return this.SkippedPositionBytes;
            }
            private set
            {
                int ch = Math.Max(1, this.Channels);
                int bytesPerFrame = ch * sizeof(float);
                long totalFrames = (this.Data?.LongLength ?? 0L) / ch;
                long totalBytes = totalFrames * bytesPerFrame;
                this.SkippedPositionBytes = Math.Clamp(value, 0, totalBytes);
            }
        }

        public long Position
        {
            get
            {
                long positionBytes = this.CurrentPlaybackPositionBytes;
                int bytesPerFrame = Math.Max(1, this.Channels) * sizeof(float);
                return bytesPerFrame > 0 ? positionBytes / bytesPerFrame : 0;
            }
        }

        public TimeSpan CurrentTime => TimeSpan.FromSeconds((double) this.Position / Math.Max(1, this.SampleRate));
        public double SizeInKb => this.Data.Length * sizeof(float) / 1024.0;

        public async Task PlayAsync(CancellationToken cancellationToken, Action? onPlaybackStopped = null, float? initialVolume = null, int desiredLatency = 50)
        {
            this.Playing = true;
            this.Paused = false;
            initialVolume ??= this.Volume / 100f;

            if (this.Data == null || this.Data.Length == 0 || this.SampleRate <= 0 || this.Channels <= 0)
            {
                this.Playing = false;
                return;
            }

            try
            {
                int bytesPerFrame = Math.Max(1, this.Channels) * sizeof(float);
                long startSampleIndex = Math.Max(0, this.StartingOffset);

                if (this.LoopEnabled && this.playbackLoopApplied && this.playbackLoopLengthBytes > 0)
                {
                    long loopStartSamples = this.playbackLoopStartBytes / sizeof(float);
                    long loopEndSamples = this.playbackLoopEndBytes / sizeof(float);
                    if (startSampleIndex < loopStartSamples || startSampleIndex >= loopEndSamples)
                    {
                        startSampleIndex = loopStartSamples;
                    }
                }

                long startFrames = Math.Max(0, startSampleIndex / Math.Max(1, this.Channels));
                this.SkippedPositionBytes = startFrames * bytesPerFrame;

                EventHandler<StoppedEventArgs>? handler = null;
                handler = (_, _) =>
                {
                    try { onPlaybackStopped?.Invoke(); }
                    finally
                    {
                        this.Playing = false;
                        this.Paused = false;
                        this.playback.PlaybackStopped -= handler!;
                    }
                };
                this.playback.PlaybackStopped += handler;

                using (cancellationToken.Register(this.playback.Stop))
                {
                    this.ComputeAndApplyLoopRegion();

                    await this.playback.InitializePlayback(
                        data: this.Data,
                        sampleRate: this.SampleRate,
                        channels: this.Channels,
                        startSampleIndex: startSampleIndex,
                        deviceSampleRate: this.SampleRate,
                        desiredLatency: desiredLatency,
                        initialVolume: initialVolume.Value).ConfigureAwait(false);

                    if (Math.Abs(this.SampleRateFactor - 1.0) > double.Epsilon)
                    {
                        await this.playback.AdjustSampleRate((float) this.SampleRateFactor).ConfigureAwait(false);
                    }

                    this.positionOriginBytes = this.playback.GetPositionBytes();
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Playback preparation was canceled");
                this.Playing = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Playback initialization failed: {ex.Message}");
                this.Playing = false;
                throw;
            }
        }

        private void ComputeAndApplyLoopRegion()
        {
            if (!this.LoopEnabled)
            {
                this.playback.ClearLoop();
                this.playbackLoopApplied = false;
                this.playbackLoopStartBytes = 0;
                this.playbackLoopEndBytes = 0;
                return;
            }

            long total = this.Data?.LongLength ?? 0L;
            long startSamples = this.loopFractionStartSamples;
            long endSamples = this.loopFractionEndSamples;

            if (endSamples <= startSamples || endSamples <= 0)
            {
                if (this.SelectionStart >= 0 && this.SelectionEnd > this.SelectionStart)
                {
                    startSamples = Math.Clamp(this.SelectionStart, 0, total);
                    endSamples = Math.Clamp(this.SelectionEnd, startSamples + 1, total);
                }
                else
                {
                    startSamples = 0;
                    endSamples = Math.Clamp(total, 1, total);
                }
            }

            this.playback.SetLoop(startSamples, endSamples);
            this.playbackLoopApplied = true;
            this.playbackLoopStartBytes = startSamples * sizeof(float);
            this.playbackLoopEndBytes = endSamples * sizeof(float);
        }

        public void UpdateLoopFraction(long baseStartSamples, long baseEndSamples, long fractionSamples, bool loopEnabled, bool adjustPosition)
        {
            bool wasLooping = this.playbackLoopApplied;
            bool disablingLoop = wasLooping && !loopEnabled;
            long resumeSamples = -1;
            if (disablingLoop && adjustPosition && this.PlayerPlaying)
            {
                resumeSamples = this.CurrentPlaybackPositionBytes / sizeof(float);
            }
            long totalSamples = this.Data?.LongLength ?? 0L;
            baseStartSamples = Math.Clamp(baseStartSamples, 0, totalSamples);
            baseEndSamples = Math.Clamp(baseEndSamples, baseStartSamples, totalSamples);
            long effectiveEnd = fractionSamples > 0
                ? Math.Min(baseStartSamples + fractionSamples, baseEndSamples)
                : baseEndSamples;

            this.LoopEnabled = loopEnabled;
            this.loopFractionStartSamples = loopEnabled ? baseStartSamples : 0;
            this.loopFractionEndSamples = loopEnabled ? effectiveEnd : 0;

            if (loopEnabled)
            {
                this.playback.SetLoop(this.loopFractionStartSamples, this.loopFractionEndSamples);
                this.playbackLoopApplied = true;
                this.playbackLoopStartBytes = this.loopFractionStartSamples * sizeof(float);
                this.playbackLoopEndBytes = this.loopFractionEndSamples * sizeof(float);

                if (adjustPosition && this.PlayerPlaying)
                {
                    int ch = Math.Max(1, this.Channels);
                    long curSamples = this.Position * ch;
                    if (curSamples < this.loopFractionStartSamples || curSamples >= this.loopFractionEndSamples)
                    {
                        this.JumpToSamples(this.loopFractionStartSamples);
                    }
                }
            }
            else
            {
                this.playback.ClearLoop(resumeSamples >= 0 ? resumeSamples : null);
                this.playbackLoopApplied = false;
                this.playbackLoopStartBytes = 0;
                this.playbackLoopEndBytes = 0;
                if (resumeSamples >= 0)
                {
                    this.JumpToSamples(resumeSamples);
                }
            }
        }

        public async Task PauseAsync()
        {
            if (this.PlayerPlaying)
            {
                long gp = 0;
                try { gp = this.playback.GetPositionBytes(); }
                catch { gp = 0; }

                long delta = Math.Max(0, gp - this.positionOriginBytes);
                int ch = Math.Max(1, this.Channels);
                int bytesPerFrame = ch * sizeof(float);
                long totalFrames = (this.Data?.LongLength ?? 0L) / ch;
                long totalBytes = totalFrames * bytesPerFrame;

                long newAbsolute = Math.Clamp(this.SkippedPositionBytes + delta, 0, totalBytes);
                this.SkippedPositionBytes = newAbsolute;
                this.playback.Pause();
                this.Playing = false;
                this.Paused = true;
                this.positionOriginBytes = gp;
                this.pausedBaselineBytes = this.SkippedPositionBytes;
                this.resumeFromSetPosition = false;
                await Task.CompletedTask;
                return;
            }

            if (this.Paused)
            {
                if (this.resumeFromSetPosition)
                {
                    int ch = Math.Max(1, this.Channels);
                    int bytesPerFrame = ch * sizeof(float);
                    long startFrames = this.SkippedPositionBytes / bytesPerFrame;
                    long startSampleIndex = startFrames * ch;
                    this.playback.SeekSamples(startSampleIndex);
                    this.playback.Resume();
                    this.Playing = true;
                    this.Paused = false;
                    try { this.positionOriginBytes = this.playback.GetPositionBytes(); }
                    catch { this.positionOriginBytes = 0; }
                    this.resumeFromSetPosition = false;
                    await Task.CompletedTask;
                    return;
                }

                try { this.positionOriginBytes = this.playback.GetPositionBytes(); }
                catch { this.positionOriginBytes = 0; }

                this.Playing = true;
                this.Paused = false;
                this.playback.Resume();
                await Task.CompletedTask;
            }
        }

        public async Task StopAsync()
        {
            this.Playing = false;
            this.Paused = false;
            this.playback.Stop();
            this.playback.ClearLoop();
            this.playbackLoopApplied = false;
            this.playbackLoopStartBytes = 0;
            this.playbackLoopEndBytes = 0;
            this.SkippedPositionBytes = 0;
            this.positionOriginBytes = 0;
            await Task.CompletedTask;
        }

        public void SetVolume(float volume)
        {
            volume = Math.Clamp(volume, 0.0f, 1.0f);
            this.Volume = volume * 100f;
            this.playback.SetVolume(volume);
        }

        public void SetPosition(long framePosition)
        {
            int channels = Math.Max(1, this.Channels);
            int bytesPerFrame = channels * sizeof(float);
            long totalFrames = (this.Data?.LongLength ?? 0L) / channels;
            long totalBytes = totalFrames * bytesPerFrame;

            long bytePosition = framePosition * (long) bytesPerFrame;
            this.SkippedPositionBytes = Math.Clamp(bytePosition, 0, totalBytes);

            if (this.Paused)
            {
                this.resumeFromSetPosition = true;
            }
        }

        public void Seek(double seconds)
        {
            long frames = (long) Math.Round(seconds * this.SampleRate);
            this.SetPosition(frames);
        }

        public async Task AdjustSampleRate(float factor)
        {
            this.SampleRateFactor = factor;
            if (this.PlayerPlaying)
            {
                await this.playback.AdjustSampleRate((float) this.SampleRateFactor).ConfigureAwait(false);
            }
        }

        public void JumpToSamples(long startSampleIndex)
        {
            int ch = Math.Max(1, this.Channels);
            if (this.Data == null || this.Data.LongLength <= 0 || ch <= 0)
            {
                return;
            }

            startSampleIndex = Math.Clamp(startSampleIndex, 0, this.Data.LongLength);
            if (this.PlayerPlaying)
            {
                try
                {
                    this.playback.SeekSamples(startSampleIndex);
                    int bytesPerFrame = ch * sizeof(float);
                    long startFrames = startSampleIndex / ch;
                    this.SkippedPositionBytes = startFrames * bytesPerFrame;
                    try { this.positionOriginBytes = this.playback.GetPositionBytes(); }
                    catch { this.positionOriginBytes = 0; }
                }
                catch
                {
                    // ignore seek errors to avoid breaking playback
                }
            }
            else
            {
                long frames = startSampleIndex / ch;
                this.SetPosition(frames);
            }
        }
    }
}
