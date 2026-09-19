using MathNet.Numerics.IntegralTransforms;
using MathNet.Numerics.Providers.FourierTransform;
using System.Numerics;

namespace ModularAudience.Audio.Processors_V4
{
    /// <summary>
    /// Uses the managed provider without changing process-wide providers or thread settings.
    /// Scaling is exactly <see cref="FourierOptions.Matlab"/>: an unscaled negative-exponent
    /// forward FFT and a positive-exponent inverse FFT divided by its own length.
    /// </summary>
    internal static class ConstantQFourier
    {
        internal static void Forward(Complex[] values, CancellationToken token)
        {
            ValidateFinite(values, token);
            if (values.Length > 1)
            {
                new ManagedFourierTransformProvider().Forward(values, FourierTransformScaling.NoScaling);
            }
            ValidateFinite(values, token);
        }

        internal static void Inverse(Complex[] values, CancellationToken token)
        {
            ValidateFinite(values, token);
            if (values.Length > 1)
            {
                new ManagedFourierTransformProvider().Backward(values, FourierTransformScaling.BackwardScaling);
            }
            ValidateFinite(values, token);
        }

        internal static bool IsFinite(Complex value)
            => double.IsFinite(value.Real) && double.IsFinite(value.Imaginary);

        private static void ValidateFinite(Complex[] values, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            for (int i = 0; i < values.Length; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                if (!IsFinite(values[i]))
                {
                    throw new ArithmeticException($"The CQT FFT contains a non-finite value at index {i}. Reduce the input or mask amplitude.");
                }
            }
            token.ThrowIfCancellationRequested();
        }
    }
}
