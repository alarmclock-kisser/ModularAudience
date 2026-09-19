using MathNet.Numerics.Providers.FourierTransform;
using System.Numerics;

namespace ModularAudience.Audio.Processors_V4
{
    internal static class IlrmaNumerics
    {
        internal const double Floor = 1e-12;
        private const long MaximumWorkingBytes = 512L * 1024 * 1024;

        internal static double Power(Complex value)
            => value.Real * value.Real + value.Imaginary * value.Imaginary;

        internal static bool IsFinite(Complex value)
            => double.IsFinite(value.Real) && double.IsFinite(value.Imaginary);

        internal static double Positive(double value, string operation)
        {
            if (!double.IsFinite(value) || value < 0)
                throw new ArithmeticException($"ILRMA {operation} produced an invalid nonnegative value.");
            return Math.Max(Floor, value);
        }

        internal static void RequireMemory(long bytes, string stage)
        {
            long available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            long budget = available > 0 ? Math.Min(MaximumWorkingBytes, available / 2) : MaximumWorkingBytes;
            if (bytes > budget)
                throw new ArgumentException($"ILRMA {stage} needs an estimated {bytes} working bytes; the limit is {budget}. Reduce AnalysisFrames, WindowSize or Threads.");
        }

        internal static void Transform(Complex[] values, bool inverse, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // A private managed provider: no process-wide MathNet provider/thread changes.
            ManagedFourierTransformProvider provider = new();
            if (inverse) provider.Backward(values, FourierTransformScaling.BackwardScaling);
            else provider.Forward(values, FourierTransformScaling.NoScaling);
            for (int i = 0; i < values.Length; i++)
            {
                if ((i & 255) == 0) token.ThrowIfCancellationRequested();
                if (!IsFinite(values[i])) throw new ArithmeticException("ILRMA FFT produced a non-finite value.");
            }
        }

        internal static double Seed(int source, int component, int index, uint salt)
        {
            // Fixed integer hashing, independent of scheduling and runtime random seeds.
            unchecked
            {
                uint value = (uint)(index + 1) * 0x9e3779b9u ^ (uint)(source + 1) * 0x85ebca6bu
                    ^ (uint)(component + 1) * 0xc2b2ae35u ^ salt;
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                value ^= value >> 16;
                return (value + 0.5) / 4294967296.0;
            }
        }
    }
}
