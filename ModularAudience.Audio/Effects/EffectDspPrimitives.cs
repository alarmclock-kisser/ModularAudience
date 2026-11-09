namespace ModularAudience.Audio.Effects;

public static partial class AudioEffectProcessor
{
    private struct Biquad
    {
        private double b0, b1, b2, a1, a2, z1, z2;
        internal double Process(double input)
        {
            double output = input * this.b0 + this.z1;
            this.z1 = input * this.b1 - output * this.a1 + this.z2;
            this.z2 = input * this.b2 - output * this.a2;
            return output;
        }

        private static Biquad Create(double b0, double b1, double b2, double a0, double a1, double a2) =>
            new() { b0 = b0 / a0, b1 = b1 / a0, b2 = b2 / a0, a1 = a1 / a0, a2 = a2 / a0 };

        internal static Biquad LowPass(double hz, int rate, double q = 0.7071067811865476)
        {
            double w = 2 * Math.PI * ClampFrequency(hz, rate) / rate, c = Math.Cos(w), alpha = Math.Sin(w) / (2 * q);
            return Create((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + alpha, -2 * c, 1 - alpha);
        }

        internal static Biquad HighPass(double hz, int rate)
        {
            double w = 2 * Math.PI * ClampFrequency(hz, rate) / rate, c = Math.Cos(w), alpha = Math.Sin(w) / Math.Sqrt(2);
            return Create((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + alpha, -2 * c, 1 - alpha);
        }

        internal static Biquad BandPass(double hz, int rate, double q)
        {
            double w = 2 * Math.PI * ClampFrequency(hz, rate) / rate, c = Math.Cos(w), alpha = Math.Sin(w) / (2 * q);
            return Create(alpha, 0, -alpha, 1 + alpha, -2 * c, 1 - alpha);
        }

        internal static Biquad Notch(double hz, int rate, double q)
        {
            double w = 2 * Math.PI * ClampFrequency(hz, rate) / rate, c = Math.Cos(w), alpha = Math.Sin(w) / (2 * q);
            return Create(1, -2 * c, 1, 1 + alpha, -2 * c, 1 - alpha);
        }
    }

    private sealed class DelayLine
    {
        private readonly double[] data;
        private int position;
        internal DelayLine(int capacity) => this.data = new double[Math.Max(4, capacity + 2)];
        internal double Read(double delay)
        {
            delay = Math.Clamp(delay, 1, this.data.Length - 2);
            double index = this.position - delay;
            if (index < 0) index += this.data.Length;
            int left = (int)index, right = (left + 1) % this.data.Length;
            return this.data[left] + (this.data[right] - this.data[left]) * (index - left);
        }
        internal void Write(double input)
        {
            this.data[this.position] = input;
            if (++this.position == this.data.Length) this.position = 0;
        }
    }

    private sealed class AllPassDelay
    {
        private readonly double[] data;
        private readonly double coefficient;
        private int position;
        internal AllPassDelay(int frames, double coefficient) { this.data = new double[Math.Max(1, frames)]; this.coefficient = coefficient; }
        internal double Process(double input)
        {
            double delayed = this.data[this.position], output = delayed - this.coefficient * input;
            this.data[this.position] = input + this.coefficient * output;
            if (++this.position == this.data.Length) this.position = 0;
            return output;
        }
    }

    /// <summary>Two offset read heads crossfade at wrap points: a true variable-rate pitch-delay, not amplitude modulation.</summary>
    private sealed class ShimmerPitchDelay
    {
        private readonly DelayLine line;
        private readonly double span, advance;
        private double phase;
        internal ShimmerPitchDelay(int rate, double ratio)
        {
            this.span = Math.Max(32, rate * 0.08); this.advance = (ratio - 1) / this.span;
            this.line = new DelayLine((int)this.span + 8);
        }
        internal double Process(double input)
        {
            this.line.Write(input);
            double second = (this.phase + 0.5) % 1;
            double weight = 0.5 - 0.5 * Math.Cos(this.phase * 2 * Math.PI);
            double result = this.line.Read(2 + (1 - this.phase) * this.span) * weight +
                this.line.Read(2 + (1 - second) * this.span) * (1 - weight);
            this.phase = (this.phase + this.advance) % 1;
            return result;
        }
    }
}
