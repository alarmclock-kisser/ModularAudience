using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.OnnxRuntime.CompileApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ModularAudience.Audio;

namespace ModularAudience.Onnx
{
    public class OnnxService : IDisposable
    {
        private static bool DebugMode => Environment.GetEnvironmentVariable("DEMUS_ONNX_DEBUG") == "1";

        public InferenceSession? _session { get; private set; } = null;

        public bool IsOnline => this._session != null;
        public string? ModelName => this._session?.ModelMetadata?.GraphName;
        // Die Standard-Reihenfolge bei htdemucs_6s
        private readonly string[] _stems = { "drums", "bass", "other", "vocals", "guitar", "piano" };
        public const int SampleRate = 44100;
        public const int ChunkSizeSeconds = 10; // 10 Sekunden pro GPU-Durchgang


        public List<string> AvailableStems => this._stems.ToList();

        public List<string> ModelDirectories { get; set; } = [
            "D:/Models/Demucs"
            ];

        public List<string> ModelPaths { get; set; } = [];

        public OnnxService(IEnumerable<string>? additionalDirectories = null, string? modelPath = null)
        {
            this.GetModelPaths(additionalDirectories?.ToArray());

            var options = new SessionOptions();
            try
            {
                // Nutzt das Microsoft.ML.OnnxRuntime.Gpu Paket
                options.AppendExecutionProvider_DML(0);
                options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            }
            catch { options.AppendExecutionProvider_CPU(); }

            if (modelPath == null)
            {
                if (this.ModelPaths.Count == 0)
                {
                    Console.WriteLine("No ONNX models found, check directories.");
                    return;
                }

                modelPath = this.ModelPaths[0]; // Nimm das erste gefundene Modell
            }


            try
            {
                this._session = new InferenceSession(modelPath, options);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing ONNX session: {ex.Message}");
                throw;
            }
        }

        public bool LoadModel(string modelPath)
        {
            try
            {
                var options = new SessionOptions();
                try
                {
                    options.AppendExecutionProvider_DML(0);
                    options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                }
                catch { options.AppendExecutionProvider_CPU(); }
                this._session?.Dispose();
                this._session = new InferenceSession(modelPath, options);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading ONNX model: {ex.Message}");
                return false;
            }
        }

        public string[] GetModelPaths(string[]? additionalDirectories = null)
        {
            if (additionalDirectories != null)
            {
                this.ModelDirectories.AddRange(additionalDirectories);
            }

            this.ModelPaths = this.ModelDirectories
                .SelectMany(dir => System.IO.Directory.GetFiles(dir, "*.onnx", SearchOption.AllDirectories))
                .ToList();

            return this.ModelPaths.ToArray();
        }



        public async Task<float[]> ExtractStemAsync(AudioObj audio, string stemName, IProgress<double>? progress = null)
        {
            if (this._session == null) throw new InvalidOperationException("ONNX Session ist nicht geladen.");

            // 1. Setup & Normalisierung 
            if (audio.SampleRate != 44100) await audio.ResampleAsync(44100);
            if (audio.Channels != 2) await audio.RechannelAsync(2);
            await audio.NormalizeAsync(1.0f); // Demucs braucht exakt Pegel zwischen -1.0 und 1.0

            float[] interleaved = audio.Data;
            int totalFrames = interleaved.Length / 2;
            int stemIdx = Array.IndexOf(this._stems, stemName.ToLower());
            if (stemIdx < 0) stemIdx = 3; // Fallback Vocals

            string inputName = _session.InputNames.First();

            // Die bewiesene magische Länge deines Modells für den Eingang!
            int framesPerChunk = 343980;

            float[] finalResult = new float[interleaved.Length];
            int processed = 0;

            // 2. Loop
            while (processed < totalFrames)
            {
                int currentChunkFrames = Math.Min(framesPerChunk, totalFrames - processed);

                // Input vorbereiten (Planar und exakt 343980 lang aufbauen)
                float[] planarInput = new float[framesPerChunk * 2];
                for (int i = 0; i < currentChunkFrames; i++)
                {
                    int srcIdx = (processed + i) * 2;
                    planarInput[i] = interleaved[srcIdx];                       // Links
                    planarInput[i + framesPerChunk] = interleaved[srcIdx + 1];  // Rechts
                }

                var inputTensor = new DenseTensor<float>(planarInput, new[] { 1, 2, framesPerChunk });
                var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) };

                // Dummy Inputs für die Hidden-States erzeugen (Verhindert Missing-Input Fehler)
                foreach (var kv in _session.InputMetadata)
                {
                    if (kv.Key == inputName) continue;
                    var dims = kv.Value.Dimensions.Select(d => d > 0 ? d : 1).ToArray();
                    if (dims.Length == 0) dims = new[] { 1 };
                    int totalElements = dims.Aggregate(1, (a, b) => a * b);
                    inputs.Add(NamedOnnxValue.CreateFromTensor(kv.Key, new DenseTensor<float>(new float[totalElements], dims)));
                }

                // 3. INFERENZ
                using var results = _session.Run(inputs);

                // -----------------------------------------------------------------------------------
                // 4. OUTPUT EXTRAKTION - DER FIX GEGEN OUT-OF-RANGE & STILLE!
                // Wir suchen explizit nach Tensoren, die groß genug sind, um Audio zu sein (> 100k)
                // -----------------------------------------------------------------------------------
                var audioOutputs = results.Where(r => r.AsTensor<float>().Length > 100000).ToList();

                if (audioOutputs.Count == 0)
                    throw new Exception("Fehler: Das Modell hat keine Audiodaten, sondern nur kleine States zurückgegeben.");

                Tensor<float> outputTensor;
                int activeStemIdx = stemIdx;

                // Prüfen, ob das Modell alle Stems separat ausgibt (Mehrere große Tensoren)
                if (audioOutputs.Count > 1)
                {
                    var match = audioOutputs.FirstOrDefault(r => r.Name.Contains(stemName, StringComparison.OrdinalIgnoreCase));
                    outputTensor = match != null ? match.AsTensor<float>() : audioOutputs[Math.Min(stemIdx, audioOutputs.Count - 1)].AsTensor<float>();
                    activeStemIdx = 0; // Wir haben den Stem schon isoliert, also Offset später auf 0
                }
                else
                {
                    // Modell gibt EINEN großen Tensor für alle Stems aus (z.B. [1, 6, 2, 343980])
                    outputTensor = audioOutputs[0].AsTensor<float>();
                }

                float[] outRaw = outputTensor.ToArray();
                var outDims = outputTensor.Dimensions.ToArray();
                int rank = outDims.Length;

                // Finde automatisch heraus, wo welche Daten liegen
                int frameAxis = Array.IndexOf(outDims, outDims.Max());
                int outFrames = outDims[frameAxis];

                // Stem-Achse (4 oder 6) finden (meist nur relevant bei zusammengefassten Tensoren)
                int stemAxis = -1;
                if (audioOutputs.Count == 1)
                {
                    stemAxis = Array.FindIndex(outDims, d => d == 4 || d == 6 || d == this._stems.Length);
                }

                // Channel-Achse (2) finden
                int channelAxis = -1;
                for (int a = 0; a < rank; a++)
                {
                    if (outDims[a] == 2 && a != stemAxis && a != frameAxis)
                    {
                        channelAxis = a; break;
                    }
                }

                // Strides (Speicher-Sprünge) berechnen
                int[] strides = new int[rank];
                int currentStride = 1;
                for (int a = rank - 1; a >= 0; a--)
                {
                    strides[a] = currentStride;
                    currentStride *= outDims[a];
                }

                int stemStride = stemAxis >= 0 ? strides[stemAxis] : 0;
                int channelStride = channelAxis >= 0 ? strides[channelAxis] : 0;
                int frameStride = frameAxis >= 0 ? strides[frameAxis] : 1;

                int safeStemIdx = stemAxis >= 0 ? Math.Min(activeStemIdx, outDims[stemAxis] - 1) : 0;
                int validCopyFrames = Math.Min(currentChunkFrames, outFrames);

                // Daten zurück in den AudioObj-Puffer schieben
                for (int i = 0; i < validCopyFrames; i++)
                {
                    int baseIdx = (safeStemIdx * stemStride) + (i * frameStride);

                    // Absoluter Sicherheitsanker gegen den IndexOutOfRangeException
                    if (baseIdx >= outRaw.Length) break;

                    float left = outRaw[baseIdx];

                    int rightIdx = baseIdx + channelStride;
                    float right = (channelAxis >= 0 && rightIdx < outRaw.Length) ? outRaw[rightIdx] : left;

                    int dstIdx = (processed + i) * 2;
                    if (dstIdx + 1 < finalResult.Length)
                    {
                        finalResult[dstIdx] = left;
                        finalResult[dstIdx + 1] = right;
                    }
                }

                processed += currentChunkFrames;
                progress?.Report((double) processed / totalFrames);
            }

            return finalResult;
        }


        public Func<AudioObj, IProgress<double>, Task<float[]>> GetPartial(string stemName)
        {
            return (audio, progress) => this.ExtractStemAsync(audio, stemName, progress);
        }

        public void Dispose() => this._session?.Dispose();
    }
}