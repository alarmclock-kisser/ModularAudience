using Microsoft.ML.OnnxRuntime.Tensors;
using System.Numerics;

namespace ModularAudience.Onnx;

// HTDemucs _spec/_ispec: normalized 4096-point FFT, periodic Hann, 1024 hop,
// reflection padding, omitted Nyquist bin and two cropped STFT frames on either side.
public static class DemucsSpectrogram
{
    private const int FftSize = 4096;
    private const int Hop = 1024;
    private const int Pad = 1536;
    private static readonly double[] Window = Enumerable.Range(0, FftSize)
        .Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / FftSize)).ToArray();

    public static DenseTensor<float> Forward(float[] planar, int length, DemucsFourier? fourier = null, CancellationToken token = default)
    {
        if (length < 2 || planar.Length != 2 * length) throw new ArgumentException("Expected planar stereo audio.");
        int frames = (length + Hop - 1) / Hop;
        var result = new DenseTensor<float>([1, 4, FftSize / 2, frames]);
        fourier ??= new();
        var spectra = new Complex[2 * frames][];
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = fourier.Workers, CancellationToken = token };
        Parallel.For(0, 2 * frames, parallel, index =>
            {
                int ch = index / frames, t = index % frames;
                var fft = spectra[index] = new Complex[FftSize];
                for (int j = 0; j < FftSize; j++)
                {
                    int sampleIndex = t * Hop + j - Pad;
                    int period = 2 * (length - 1);
                    sampleIndex = ((sampleIndex % period) + period) % period;
                    if (sampleIndex >= length) sampleIndex = period - sampleIndex;
                    fft[j] = new Complex(planar[ch * length + sampleIndex] * Window[j], 0);
                }
            });
        fourier.Transform(spectra, false, token);
        Parallel.For(0, 2 * frames, parallel, index =>
            {
                int ch = index / frames, t = index % frames;
                var fft = spectra[index];
                for (int f = 0; f < FftSize / 2; f++)
                {
                    result[0, 2 * ch, f, t] = (float)(fft[f].Real / 64);
                    result[0, 2 * ch + 1, f, t] = (float)(fft[f].Imaginary / 64);
                }
            });
        return result;
    }

    public static float[] Inverse(Tensor<float> spectrum, int stem, int length, DemucsFourier? fourier = null, CancellationToken token = default)
    {
        int frames = (length + Hop - 1) / Hop;
        if (spectrum.Dimensions.Length != 5 || spectrum.Dimensions[0] != 1
            || spectrum.Dimensions[2] != 4 || spectrum.Dimensions[3] != FftSize / 2
            || spectrum.Dimensions[4] != frames || stem < 0 || stem >= spectrum.Dimensions[1])
            throw new NotSupportedException("Expected HTDemucs spectrum [1, stems, 4, 2048, frames].");
        var result = new float[2 * length];
        var weights = new double[length];
        fourier ??= new();
        var spectra = new Complex[2 * frames][];
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = fourier.Workers, CancellationToken = token };
        // The two zero frames restored at each end still contribute to ISTFT's window denominator.
        for (int t = -2; t < frames + 2; t++)
            for (int j = 0; j < FftSize; j++)
            {
                int index = t * Hop + j - Pad;
                if ((uint)index < (uint)length) weights[index] += Window[j] * Window[j];
            }
        Parallel.For(0, 2 * frames, parallel, index =>
            {
                int ch = index / frames, t = index % frames;
                var fft = spectra[index] = new Complex[FftSize];
                fft[FftSize / 2] = Complex.Zero;
                for (int f = 0; f < FftSize / 2; f++)
                {
                    fft[f] = new Complex(spectrum[0, stem, 2 * ch, f, t], spectrum[0, stem, 2 * ch + 1, f, t]);
                    if (f > 0) fft[FftSize - f] = Complex.Conjugate(fft[f]);
                }
            });
        fourier.Transform(spectra, true, token);
        // Four disjoint hop phases: no shared output writes within each parallel pass.
        for (int phase = 0; phase < 4; phase++)
        {
            int activePhase = phase;
            Parallel.For(0, 2 * frames, parallel, index =>
            {
                int ch = index / frames, t = index % frames;
                if (t % 4 != activePhase) return;
                var fft = spectra[index];
                for (int j = 0; j < FftSize; j++)
                {
                    int sampleIndex = t * Hop + j - Pad;
                    if ((uint)sampleIndex < (uint)length)
                        result[2 * sampleIndex + ch] += (float)(fft[j].Real * 64 * Window[j] / weights[sampleIndex]);
                }
            });
        }
        return result;
    }

    public static Task<DenseTensor<float>> ForwardAsync(float[] planar, int length, DemucsFourier? fourier = null, CancellationToken token = default) =>
        Task.Run(() => Forward(planar, length, fourier, token), token);

    public static Task<float[]> InverseAsync(Tensor<float> spectrum, int stem, int length, DemucsFourier? fourier = null, CancellationToken token = default) =>
        Task.Run(() => Inverse(spectrum, stem, length, fourier, token), token);
}
