using NAudio.Dsp;
using NAudio.Wave;

namespace ModularAudience.Audio.Processing
{
    internal sealed class VarispeedSampleProvider : ISampleProvider
    {
        private const int RateBlockFrames = 32;
        private readonly ISampleProvider source;
        private readonly WdlResampler resampler = new();
        private readonly PlaybackPositionTimeline position = new();
        private readonly int rampFrames;
        private float targetRate;
        private float rampTarget;
        private double currentRate;
        private double rateStep;
        private int remainingRampFrames;

        public WaveFormat WaveFormat { get; }
        internal double CurrentRate => this.currentRate;

        public VarispeedSampleProvider(ISampleProvider source, int outputSampleRate, double rate)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(outputSampleRate, source.WaveFormat.Channels);
            this.rampFrames = Math.Max(1, outputSampleRate / 100);
            this.SetTargetRate((float) rate);
            this.currentRate = this.rampTarget = this.targetRate;
            // Keep one filter topology on both sides of unity; moving an IIR filter between input
            // and output at 1x would reuse its history for a different signal.
            this.resampler.SetMode(true, 0, true, sinc_size: 64, sinc_interpsize: 16);
            this.resampler.SetFeedMode(false);
            this.resampler.SetRates(source.WaveFormat.SampleRate * this.currentRate, outputSampleRate);
        }

        public void SetTargetRate(float rate)
        {
            if (!float.IsFinite(rate)) { throw new ArgumentOutOfRangeException(nameof(rate)); }
            Volatile.Write(ref this.targetRate, Math.Clamp(rate, 0.5f, 2f));
        }

        public int Read(float[] buffer, int offset, int count) => this.Read(buffer.AsSpan(offset, count));

        public int Read(Span<float> buffer)
        {
            int channels = this.WaveFormat.Channels;
            int requestedFrames = buffer.Length / channels;
            int writtenFrames = 0;
            while (writtenFrames < requestedFrames)
            {
                this.UpdateRamp();
                int frames = Math.Min(RateBlockFrames, requestedFrames - writtenFrames);
                if (this.remainingRampFrames > 0) { frames = Math.Min(frames, this.remainingRampFrames); }
                double rate = this.currentRate + this.rateStep * frames * 0.5;
                int written = this.ReadBlock(buffer.Slice(writtenFrames * channels, frames * channels), frames, rate);
                this.AdvanceRamp(written);
                this.position.Add(written, rate * this.source.WaveFormat.SampleRate / this.WaveFormat.SampleRate);
                writtenFrames += written;
                if (written < frames) { break; }
            }
            return writtenFrames * channels;
        }

        private void UpdateRamp()
        {
            float target = Volatile.Read(ref this.targetRate);
            if (target == this.rampTarget) { return; }
            this.rampTarget = target;
            this.remainingRampFrames = this.rampFrames;
            this.rateStep = (target - this.currentRate) / this.rampFrames;
        }

        private int ReadBlock(Span<float> buffer, int frames, double rate)
        {
            int channels = this.WaveFormat.Channels;
            this.resampler.SetRates(this.source.WaveFormat.SampleRate * rate, this.WaveFormat.SampleRate);
            int needed = this.resampler.ResamplePrepare(frames, channels, out Span<float> input);
            int supplied = this.source.Read(input[..(needed * channels)]) / channels;
            return this.resampler.ResampleOut(buffer, supplied, frames, channels);
        }

        private void AdvanceRamp(int frames)
        {
            if (this.remainingRampFrames == 0) { return; }
            this.currentRate += this.rateStep * frames;
            this.remainingRampFrames -= frames;
            if (this.remainingRampFrames == 0)
            {
                this.currentRate = this.rampTarget;
                this.rateStep = 0;
            }
        }

        internal double GetSourceFramePosition(long outputFramePosition) => this.position.GetSourceFramePosition(outputFramePosition);
    }
}
