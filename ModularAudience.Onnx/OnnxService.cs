using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ModularAudience.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ModularAudience.Onnx
{
    public sealed class OnnxService : IDisposable
    {
        public InferenceSession? _session { get; private set; }
        public bool IsOnline => this._session != null;
        public string? ModelName => this._session?.ModelMetadata.GraphName;
        public string? ModelPath { get; private set; }
        public string ExecutionProvider { get; private set; } = "CPU";
        public string? LoadWarning { get; private set; }
        public DemucsLoadOptions? EffectiveOptions { get; private set; }
        private DemucsFourier fourier = new();
        public string FourierBackend => this.fourier.Backend;
        public const int SampleRate = 44100;
        public const int ChunkSizeSeconds = 10;
        private static readonly string[] Stems = ["drums", "bass", "other", "vocals", "guitar", "piano"];
        public List<string> AvailableStems => Stems.Take(this.stemCount).ToList();
        public List<string> ModelDirectories { get; set; } = ["D:/Models/Demucs"];
        public List<string> ModelPaths { get; set; } = [];
        private int stemCount = 6;
        private int chunkFrames;
        private string waveformInput = "";
        private string? spectrumInput;
        private string waveformOutput = "";
        private string? spectrumOutput;

        public OnnxService(IEnumerable<string>? additionalDirectories = null, string? modelPath = null,
            bool loadImmediately = true)
        {
            this.GetModelPaths(additionalDirectories?.ToArray());
            if (loadImmediately && (modelPath ?? this.ModelPaths.FirstOrDefault()) is string path)
                this.LoadModel(path);
        }

        public bool LoadModel(string modelPath, DemucsLoadOptions? loadOptions = null)
        {
            loadOptions = JsonSerializer.Deserialize<DemucsLoadOptions>(JsonSerializer.Serialize(loadOptions ?? new()))!;
            this.EffectiveOptions = null;
            string? fallbackWarning = null;
            InferenceSession? session = null;
            try
            {
                try
                {
                    using var options = loadOptions.Create(loadOptions.UseDirectML);
                    session = new InferenceSession(modelPath, options);
                    this.ExecutionProvider = loadOptions.UseDirectML ? $"DirectML (device {loadOptions.DeviceId})" : "CPU";
                }
                catch (OnnxRuntimeException error) when (loadOptions.UseDirectML && loadOptions.AllowCpuFallback)
                {
                    using var cpuOptions = loadOptions.Create(false);
                    session = new InferenceSession(modelPath, cpuOptions);
                    this.ExecutionProvider = "CPU";
                    fallbackWarning = $"DirectML could not initialize: {error.Message} CPU is used instead.";
                }

                // This export has two real inputs/outputs: waveform and complex-as-channels STFT.
                // Never guess an audio axis from the largest dimension or fill unknown inputs with zeros.
                var waveInputs = session.InputMetadata.Where(p => p.Value.ElementType == typeof(float)
                    && p.Value.Dimensions is [1, 2, > 0]).ToArray();
                if (waveInputs.Length != 1)
                    throw new NotSupportedException("Expected one float waveform input shaped [1, 2, samples].");
                var wave = waveInputs[0];
                int frames = wave.Value.Dimensions[2];
                var specInputs = session.InputMetadata.Where(p => p.Value.ElementType == typeof(float)
                    && p.Value.Dimensions is [1, 4, 2048, > 0]).ToArray();
                bool hybrid = specInputs.Length == 1;
                if (session.InputMetadata.Count != (hybrid ? 2 : 1)
                    || (hybrid && specInputs[0].Value.Dimensions[3] != (frames + 1023) / 1024))
                    throw new NotSupportedException("Unsupported Demucs inputs. Expected waveform and optional HTDemucs complex STFT.");
                var specOutputs = session.OutputMetadata.Where(p => p.Value.ElementType == typeof(float)
                    && p.Value.Dimensions is [1, 4 or 6, 4, 2048, > 0]).ToArray();
                var waveOutputs = session.OutputMetadata.Where(p => p.Value.ElementType == typeof(float)
                    && p.Value.Dimensions.Length == 4).ToArray();
                if (waveOutputs.Length != 1 || (hybrid && specOutputs.Length != 1))
                    throw new NotSupportedException("Unsupported Demucs outputs. Expected [1, stems, 2, samples] and optional complex STFT.");

                this._session?.Dispose();
                this._session = session;
                this.waveformInput = wave.Key;
                this.spectrumInput = hybrid ? specInputs[0].Key : null;
                this.waveformOutput = waveOutputs[0].Key;
                this.spectrumOutput = hybrid ? specOutputs[0].Key : null;
                this.stemCount = hybrid ? specOutputs[0].Value.Dimensions[1]
                    : waveOutputs[0].Value.Dimensions[1] is 4 or 6 ? waveOutputs[0].Value.Dimensions[1] : 6;
                this.chunkFrames = frames;
                this.ModelPath = modelPath;
                this.LoadWarning = fallbackWarning;
                this.fourier = new DemucsFourier(loadOptions);
                if (loadOptions.UseDirectML && this.ExecutionProvider.StartsWith("DirectML") && loadOptions.ValidateDirectML)
                    this.ValidateDirectML(modelPath, loadOptions);
                loadOptions.UseDirectML = this.ExecutionProvider.StartsWith("DirectML", StringComparison.Ordinal);
                if (loadOptions.UseDirectML)
                {
                    loadOptions.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                    loadOptions.EnableMemoryPattern = false;
                }
                this.EffectiveOptions = loadOptions;
                return true;
            }
            catch
            {
                session?.Dispose();
                if (ReferenceEquals(session, this._session)) this._session = null;
                throw;
            }
        }

        public string[] GetModelPaths(string[]? additionalDirectories = null)
        {
            if (additionalDirectories != null) this.ModelDirectories.AddRange(additionalDirectories);
            this.ModelPaths = this.ModelDirectories.Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(Directory.Exists)
                .SelectMany(dir => Directory.EnumerateFiles(dir, "*.onnx", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true
                }))
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
            return this.ModelPaths.ToArray();
        }

        public async Task<Dictionary<string, float[]>> ExtractStemsAsync(AudioObj audio,
            IEnumerable<string> stemNames, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            var session = this._session ?? throw new InvalidOperationException("The ONNX model is not loaded.");
            string[] selected = stemNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            int[] indices = selected.Select(name => this.AvailableStems.FindIndex(s => s.Equals(name, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (indices.Any(i => i < 0)) throw new ArgumentException("The selected stem is not available in this model.");
            if (selected.Length == 0) throw new ArgumentException("Select at least one stem.");

            // Prepare a private copy. Separation must never resample or normalize the source track.
            using AudioObj prepared = await audio.CloneAsync().ConfigureAwait(false);
            if (prepared.SampleRate != SampleRate)
            {
                prepared.Data = Resample(prepared.Data, prepared.SampleRate, SampleRate, prepared.Channels);
                prepared.SampleRate = SampleRate;
            }
            if (prepared.Channels != 2) await prepared.RechannelAsync(2).ConfigureAwait(false);
            float[] data = prepared.Data;
            if (data.Any(value => !float.IsFinite(value))) throw new ArgumentException("The source contains non-finite audio samples.");
            int totalFrames = data.Length / 2;
            var result = selected.ToDictionary(name => name, _ => new float[data.Length]);
            var weights = new float[totalFrames];

            // Match Demucs apply_model: normalize using the mono reference, not peak normalization.
            double mean = 0;
            for (int i = 0; i < totalFrames; i++) mean += (data[2 * i] + (double)data[2 * i + 1]) / 2;
            mean /= Math.Max(1, totalFrames);
            double variance = 0;
            for (int i = 0; i < totalFrames; i++)
            {
                double value = (data[2 * i] + (double)data[2 * i + 1]) / 2 - mean;
                variance += value * value;
            }
            double std = Math.Sqrt(variance / Math.Max(1, totalFrames - 1));
            // Anti-phase stereo can have a silent mono reference despite containing audio.
            if (std < 1e-8) std = Math.Sqrt(data.Sum(v => (double)v * v) / Math.Max(1, data.Length));
            if (std > 1e-8)
            {
                int overlap = (int)Math.Round(this.chunkFrames * (this.EffectiveOptions?.ChunkOverlap ?? 0.25));
                int step = this.chunkFrames - overlap;
                for (int start = 0; start < totalFrames; start += step)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = Math.Min(this.chunkFrames, totalFrames - start);
                    float[] planar = new float[2 * this.chunkFrames];
                    for (int i = 0; i < count; i++)
                        for (int ch = 0; ch < 2; ch++)
                            planar[ch * this.chunkFrames + i] = (float)((data[2 * (start + i) + ch] - mean) / std);
                    var inputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor(this.waveformInput, new DenseTensor<float>(planar, [1, 2, this.chunkFrames]))
                    };
                    if (this.spectrumInput != null)
                        inputs.Add(NamedOnnxValue.CreateFromTensor(this.spectrumInput,
                            await DemucsSpectrogram.ForwardAsync(planar, this.chunkFrames, this.fourier, cancellationToken).ConfigureAwait(false)));
                    using var outputs = session.Run(inputs);
                    cancellationToken.ThrowIfCancellationRequested();
                    var temporal = outputs.Single(o => o.Name == this.waveformOutput).AsTensor<float>();
                    if (Environment.GetEnvironmentVariable("DEMUS_ONNX_DEBUG") == "1")
                        foreach (var output in outputs)
                        {
                            var values = output.AsTensor<float>();
                            Console.WriteLine($"{this.ExecutionProvider} {output.Name} [{string.Join(",", values.Dimensions.ToArray())}] RMS={Math.Sqrt(values.Sum(v => (double)v * v) / values.Length)} peak={values.Max(v => Math.Abs(v))}");
                        }
                    if (!temporal.Dimensions.SequenceEqual(new[] { 1, this.stemCount, 2, this.chunkFrames }))
                        throw new NotSupportedException($"Unsupported waveform output shape: [{string.Join(", ", temporal.Dimensions.ToArray())}].");
                    Tensor<float>? spectral = this.spectrumOutput == null ? null
                        : outputs.Single(o => o.Name == this.spectrumOutput).AsTensor<float>();
                    for (int s = 0; s < selected.Length; s++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        float[]? frequencyAudio = spectral == null ? null
                            : await DemucsSpectrogram.InverseAsync(spectral, indices[s], this.chunkFrames, this.fourier, cancellationToken).ConfigureAwait(false);
                        float[] destination = result[selected[s]];
                        for (int i = 0; i < count; i++)
                        {
                            float weight = overlap == 0 ? 1 : Math.Min(Math.Min(i + 1, this.chunkFrames - i), overlap) / (float)overlap;
                            for (int ch = 0; ch < 2; ch++)
                            {
                                double value = temporal[0, indices[s], ch, i] + (frequencyAudio?[2 * i + ch] ?? 0);
                                float sample = (float)(value * std + mean);
                                if (!float.IsFinite(sample)) throw new ArithmeticException("The model produced non-finite audio samples.");
                                destination[2 * (start + i) + ch] += sample * weight;
                            }
                        }
                    }
                    for (int i = 0; i < count; i++)
                        weights[start + i] += overlap == 0 ? 1 : Math.Min(Math.Min(i + 1, this.chunkFrames - i), overlap) / (float)overlap;
                    progress?.Report(Math.Min(1, (double)(start + count) / totalFrames));
                    if (start + count == totalFrames) break;
                }
                foreach (float[] stem in result.Values)
                    for (int i = 0; i < totalFrames; i++)
                        for (int ch = 0; ch < 2; ch++) stem[2 * i + ch] /= weights[i];
            }

            // Return the original format so callers can retain the original track's metadata.
            foreach (string name in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var converted = new AudioObj { Data = result[name], SampleRate = SampleRate, Channels = 2 };
                if (audio.SampleRate != SampleRate)
                    converted.Data = Resample(converted.Data, SampleRate, audio.SampleRate, 2);
                if (audio.Channels != 2) await converted.RechannelAsync(audio.Channels).ConfigureAwait(false);
                float[] samples = converted.Data;
                if (samples.Length != audio.Data.Length) Array.Resize(ref samples, audio.Data.Length);
                result[name] = samples;
            }
            progress?.Report(1);
            return result;
        }

        public async Task<float[]> ExtractStemAsync(AudioObj audio, string stemName, IProgress<double>? progress = null) =>
            (await this.ExtractStemsAsync(audio, [stemName], progress).ConfigureAwait(false))[stemName];

        // A complete pass per stem intentionally favors time-to-first-track over total throughput.
        // Yield only complete, format-restored audio; already yielded tracks survive cancellation.
        public async IAsyncEnumerable<KeyValuePair<string, float[]>> ExtractStemsSequentialAsync(AudioObj audio,
            IEnumerable<string> stemNames, IProgress<double>? progress = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            string[] selected = stemNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (selected.Length == 0) throw new ArgumentException("Select at least one stem.");
            if (selected.Any(name => !this.AvailableStems.Contains(name, StringComparer.OrdinalIgnoreCase)))
                throw new ArgumentException("The selected stem is not available in this model.");
            foreach (string stem in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await this.ExtractStemsAsync(audio, [stem], progress, cancellationToken).ConfigureAwait(false);
                yield return new(stem, result[stem]);
            }
        }

        public Func<AudioObj, IProgress<double>, Task<float[]>> GetPartial(string stemName) =>
            (audio, progress) => this.ExtractStemAsync(audio, stemName, progress);

        private sealed class BufferProvider(float[] data, int sampleRate, int channels) : ISampleProvider
        {
            private int position;
            public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            public int Read(float[] buffer, int offset, int count)
                => this.Read(buffer.AsSpan(offset, count));
            public int Read(Span<float> buffer)
            {
                int length = Math.Min(buffer.Length, data.Length - this.position);
                data.AsSpan(this.position, length).CopyTo(buffer);
                this.position += length;
                return length;
            }
        }

        private static float[] Resample(float[] data, int sourceRate, int targetRate, int channels)
        {
            var source = new BufferProvider(data, sourceRate, channels);
            var resampler = new WdlResamplingSampleProvider(source, targetRate);
            var result = new float[checked((int)Math.Round((double)(data.Length / channels) * targetRate / sourceRate) * channels)];
            int position = 0;
            while (position < result.Length)
            {
                int read = resampler.Read(result.AsSpan(position));
                if (read == 0) break;
                position += read;
            }
            return result;
        }

        private void ValidateDirectML(string modelPath, DemucsLoadOptions options)
        {
            var planar = new float[2 * this.chunkFrames];
            for (int i = 0; i < this.chunkFrames; i++)
            {
                planar[i] = (float)(0.2 * Math.Sin(2 * Math.PI * 110 * i / SampleRate));
                planar[this.chunkFrames + i] = planar[i] * 0.8f;
            }
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(this.waveformInput, new DenseTensor<float>(planar, [1, 2, this.chunkFrames]))
            };
            if (this.spectrumInput != null)
                inputs.Add(NamedOnnxValue.CreateFromTensor(this.spectrumInput, DemucsSpectrogram.Forward(planar, this.chunkFrames, this.fourier)));
            using var cpuOptions = options.Create(false);
            var cpu = new InferenceSession(modelPath, cpuOptions);
            try
            {
                bool valid = true;
                using (var expected = cpu.Run(inputs))
                using (var actual = this._session!.Run(inputs))
                    foreach (var reference in expected)
                    {
                        float[] a = reference.AsTensor<float>().ToArray();
                        float[] b = actual.Single(v => v.Name == reference.Name).AsTensor<float>().ToArray();
                        double energy = 0, error = 0;
                        for (int i = 0; i < a.Length; i++)
                        {
                            energy += (double)a[i] * a[i];
                            error += (a[i] - (double)b[i]) * (a[i] - (double)b[i]);
                        }
                        if (!double.IsFinite(error) || Math.Sqrt(error / Math.Max(energy, 1e-12)) > 0.05) valid = false;
                    }
                if (valid) return;
                if (!options.AllowCpuFallback)
                    throw new InvalidOperationException("DirectML output differs significantly from CPU on this device. Choose CPU or enable CPU fallback.");
                this._session!.Dispose();
                this._session = cpu;
                cpu = null!;
                this.ExecutionProvider = "CPU (DirectML verification fallback)";
                this.LoadWarning = "DirectML produced incorrect values on the selected device. The verified CPU session is used instead.";
            }
            finally { cpu?.Dispose(); }
        }

        public void Dispose()
        {
            this._session?.Dispose();
            this._session = null;
        }
    }
}
