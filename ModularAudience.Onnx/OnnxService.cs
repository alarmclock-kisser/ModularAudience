using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.OnnxRuntime.CompileApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Services
{
    public class DemucsOnnxService : IDisposable
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

        public DemucsOnnxService(IEnumerable<string>? additionalDirectories = null, string? modelPath = null)
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
            if (this._session == null)
            {
                Console.WriteLine("ONNX session not initialized.");
                return Array.Empty<float>();
            }

            // 1. Vorbereitung über dein AudioObj
            // Da du meintest, es hat Methoden zum Resamplen/Rechanneln:
            await audio.ResampleAsync(SampleRate);
            // Sicherstellen, dass wir Stereo haben (Demucs Standard)
            if (audio.Channels != 2)
            {
                await audio.RechannelAsync(2).ConfigureAwait(false);
            }

            // Only scale input if values exceed expected float range [-1..1].
            // Avoid always normalizing — that can amplify noise. Scale down if peak > 1.0.
            try
            {
                var localData = audio.Data;
                float peak = 0f;
                for (int i = 0; i < localData.Length; i++)
                {
                    float v = Math.Abs(localData[i]);
                    if (v > peak) peak = v;
                }

                if (peak > 1.0f)
                {
                    float scale = 1.0f / peak;
                    for (int i = 0; i < localData.Length; i++) localData[i] *= scale;
                    Console.WriteLine($"ONNX: Input peak {peak:F3} - scaled by {scale:F6}");
                }
            }
            catch { /* non-fatal */ }

            float[] inputData = audio.Data;
            int totalSamples = inputData.Length / 2;
            int samplesPerChunk = SampleRate * ChunkSizeSeconds;
            float[] finalResultInterleaved = new float[inputData.Length];
            // For overlap-add stitching use accumulator and per-frame weight buffer
            float[] accum = new float[inputData.Length];
            float[] weight = new float[totalSamples];

            int stemIdx = Array.IndexOf(this._stems, stemName.ToLower());
            if (stemIdx == -1) throw new ArgumentException($"Stem {stemName} nicht bekannt.");

            // 1) Vor der Chunk-Schleife: ermittle erwartete Samples aus dem Modell
            int expectedSamples = samplesPerChunk; // default fallback
            try
            {
                var inputName = this._session.InputNames.First();
                var meta = this._session.InputMetadata[inputName];
                var dims = meta.Dimensions; // NodeMetadata.Dimensions (prüfen ob >0)
                if (dims != null && dims.Length >= 3 && dims[2] > 0)
                {
                    expectedSamples = dims[2];
                }
            }
            catch { /* safe fallback */ }

            // 2) In der Chunk-Schleife: paddende Eingabe bauen
            int steps = (int)Math.Ceiling((double)totalSamples / samplesPerChunk);
            for (int startSample = 0; startSample < totalSamples; startSample += samplesPerChunk)
            {
                progress?.Report((double)startSample / totalSamples * 0.9); // Fortschritt vor dem Modelllauf (90%)

                int currentChunkSamples = Math.Min(samplesPerChunk, totalSamples - startSample);
                float[] planarSmall = this.PreparePlanarChunk(inputData, startSample, currentChunkSamples);

                // The model may expect a different number of samples than our processing chunk.
                // If our current chunk is larger than the model input, split it into model-sized
                // subchunks (last one padded) and run the model for each subchunk.
                int modelFrames = expectedSamples;
                int planarFrames = currentChunkSamples;
                int processed = 0;

                while (processed < planarFrames)
                {
                    int sliceFrames = Math.Min(modelFrames, planarFrames - processed);

                    // planar layout: [L0..L(n-1), R0..R(n-1)]
                    // model input expects [L0..L(m-1), R0..R(m-1)] with m == modelFrames
                    float[] planarPadded = new float[modelFrames * 2];

                    // copy left channel slice
                    Array.Copy(planarSmall, processed, planarPadded, 0, sliceFrames);
                    // copy right channel slice (right block starts at planarFrames)
                    Array.Copy(planarSmall, planarFrames + processed, planarPadded, modelFrames, sliceFrames);

                    // Safety: if chunk amplitude exceeds [-1,1], scale down to avoid model clipping/muting
                    float peak = 0f;
                    for (int i = 0; i < planarPadded.Length; i++)
                    {
                        float v = Math.Abs(planarPadded[i]);
                        if (v > peak) peak = v;
                    }
                    if (peak > 1.0f)
                    {
                        float scale = 1.0f / peak;
                        for (int i = 0; i < planarPadded.Length; i++) planarPadded[i] *= scale;
                    }

                    var inputTensor = new DenseTensor<float>(planarPadded, new[] { 1, 2, modelFrames });

                    // Build inputs for all model inputs. Some models expect additional
                    // non-audio tensors (e.g. masks, lengths, or config tensors). If
                    // those are not provided, ONNX Runtime can fail inside internal
                    // nodes (ReduceMean, etc.) with "Missing Input" messages.
                    var inputs = new List<NamedOnnxValue>();
                    var inputMeta = this._session.InputMetadata; // IReadOnlyDictionary<string, NodeMetadata>
                    var audioInputName = this._session.InputNames.First();

                    // Add the actual audio tensor for the primary input
                    inputs.Add(NamedOnnxValue.CreateFromTensor(audioInputName, inputTensor));

                    // For any other declared inputs, create a safe zero-filled tensor
                    foreach (var kv in inputMeta)
                    {
                        var name = kv.Key;
                        if (name == audioInputName) continue;

                        var dims = kv.Value.Dimensions ?? Array.Empty<int>();
                        // Replace unknown/variable dims with 1 as a safe fallback
                        var safeDims = dims.Select(d => d > 0 ? d : 1).ToArray();
                        if (safeDims.Length == 0) safeDims = new[] { 1 };

                        int total = safeDims.Length == 0 ? 1 : safeDims.Aggregate(1, (a, b) => a * b);
                        var dummy = new float[total]; // zero-filled
                        var dummyTensor = new DenseTensor<float>(dummy, safeDims);
                        inputs.Add(NamedOnnxValue.CreateFromTensor(name, dummyTensor));
                    }


                    // Prefer an output that is a time-domain tensor shaped [1, stems, 2, frames]
                    string[] outputNames;
                    try
                    {
                        var preferred = new List<string>();
                        foreach (var kv in this._session.OutputMetadata)
                        {
                            var name = kv.Key;
                            var dims = kv.Value.Dimensions ?? Array.Empty<int>();
                            if (dims.Length >= 4 && dims[1] == this._stems.Length && dims[2] == 2)
                            {
                                preferred.Add(name);
                            }
                        }
                        outputNames = preferred.Count > 0 ? preferred.ToArray() : this._session.OutputNames.ToArray();
                    }
                    catch
                    {
                        outputNames = this._session.OutputNames.ToArray();
                    }

                    using var results = this._session.Run(inputs, outputNames);

                    var resultList = results.ToList();
                    if (resultList.Count == 0)
                    {
                        throw new InvalidOperationException("ONNX run returned no outputs. Check model outputs and provided input tensors.");
                    }

                    // Prefer a time-domain tensor with shape like [1, stems, channels, frames]
                    Tensor<float>? chosenTensor = null;
                    int bestFrames = -1;

                    foreach (var r in resultList)
                    {
                        Tensor<float>? t = null;
                        try { t = r.AsTensor<float>(); } catch { continue; }
                        var dims = t.Dimensions.ToArray();
                        if (dims.Length < 2) continue;

                        int stemsDim = dims.Length > 1 ? dims[1] : 1;
                        int channelsDim = dims.Length >= 3 ? dims[2] : 1;
                        int framesDim = dims.Length >= 4 ? dims[^1] : (dims.Length == 3 ? dims[2] : (dims.Length == 2 ? dims[1] : 0));

                        // Prefer: dims[1] == stems && channels == 2 (stereo time-domain). Choose the one with the most frames.
                        if (stemsDim == this._stems.Length && channelsDim == 2)
                        {
                            if (framesDim > bestFrames)
                            {
                                chosenTensor = t;
                                bestFrames = framesDim;
                            }
                        }
                    }

                    // If none matched stereo packed stems, accept any packed stems tensor (choose largest frames)
                    if (chosenTensor == null)
                    {
                        foreach (var r in resultList)
                        {
                            Tensor<float>? t = null;
                            try { t = r.AsTensor<float>(); } catch { continue; }
                            var dims = t.Dimensions.ToArray();
                            int stemsDim = dims.Length > 1 ? dims[1] : 1;
                            int framesDim = dims.Length >= 4 ? dims[^1] : (dims.Length == 3 ? dims[2] : (dims.Length == 2 ? dims[1] : 0));
                            if (stemsDim == this._stems.Length)
                            {
                                if (framesDim > bestFrames)
                                {
                                    chosenTensor = t;
                                    bestFrames = framesDim;
                                }
                            }
                        }
                    }

                    // Fallbacks: if still null, use per-output mapping if count matches stems, else first tensor
                    if (chosenTensor == null)
                    {
                        if (resultList.Count == this._stems.Length)
                        {
                            chosenTensor = resultList[stemIdx].AsTensor<float>();
                        }
                        else
                        {
                            chosenTensor = resultList.First().AsTensor<float>();
                        }
                    }

                    // Determine dimensions robustly: channels is dims[2] if present, frames is last dimension
                    var dimsChosen = chosenTensor.Dimensions.ToArray();
                    int channels = dimsChosen.Length >= 3 ? dimsChosen[2] : 1;
                    int frames = dimsChosen.Length >= 2 ? dimsChosen[^1] : 0;

                    var flat = chosenTensor.ToArray();

                    // Compute generic strides for robust indexing regardless of dim order
                    int rank = dimsChosen.Length;
                    var strides = new int[rank];
                    for (int k = 0; k < rank; k++)
                    {
                        int prod = 1;
                        for (int j = k + 1; j < rank; j++) prod *= Math.Max(1, dimsChosen[j]);
                        strides[k] = prod;
                    }

                    // Identify stem, channel and frame axes heuristically
                    int stemAxis = -1;
                    int channelAxis = -1;
                    int frameAxis = -1;

                    for (int k = 0; k < rank; k++)
                    {
                        if (stemAxis == -1 && dimsChosen[k] == this._stems.Length) stemAxis = k;
                        if (channelAxis == -1 && dimsChosen[k] == 2) channelAxis = k;
                    }

                    // frame axis: choose largest remaining dim (excluding batch/stem/channel)
                    int maxDim = -1;
                    for (int k = 0; k < rank; k++)
                    {
                        if (k == 0) continue; // skip batch if present
                        if (k == stemAxis || k == channelAxis) continue;
                        if (dimsChosen[k] > maxDim)
                        {
                            maxDim = dimsChosen[k];
                            frameAxis = k;
                        }
                    }

                    // Fallbacks
                    if (stemAxis == -1)
                    {
                        // assume axis 1
                        stemAxis = Math.Min(1, rank - 1);
                    }
                    if (channelAxis == -1)
                    {
                        // try common positions
                        channelAxis = rank >= 3 ? 2 : rank - 1;
                    }
                    if (frameAxis == -1)
                    {
                        frameAxis = rank - 1;
                    }

                    int availableFrames = dimsChosen[frameAxis];
                    int copyFrames = Math.Min(sliceFrames, availableFrames);

                    // Common case: dims [1, stems, channels=2, frames]
                    if (dimsChosen.Length >= 4 && dimsChosen[1] == this._stems.Length)
                    {
                        // handle both layouts: [1, stems, channels, frames] (channels-first)
                        // and [1, stems, frames, channels] (channels-last)
                        if (dimsChosen[2] == 2)
                        {
                            // channels-first: [1, stems, 2, frames]
                            int framesDim = dimsChosen[3];
                            int stemBlock = 2 * framesDim;
                            int baseOffset = stemIdx * stemBlock;

                            double sumL = 0.0, sumR = 0.0;
                            for (int i = 0; i < copyFrames; i++)
                            {
                                int leftIndex = baseOffset + 0 * framesDim + i;
                                int rightIndex = baseOffset + 1 * framesDim + i;
                                if (leftIndex < 0 || leftIndex >= flat.Length) continue;
                                sumL += flat[leftIndex];
                                if (rightIndex >= 0 && rightIndex < flat.Length) sumR += flat[rightIndex];
                                else sumR += flat[leftIndex];
                            }
                            double meanL = copyFrames > 0 ? sumL / copyFrames : 0.0;
                            double meanR = copyFrames > 0 ? sumR / copyFrames : 0.0;

                            // Create Hann window for overlap-add to smooth chunk boundaries
                            var win = new float[copyFrames];
                            if (copyFrames > 1)
                            {
                                for (int n = 0; n < copyFrames; n++)
                                {
                                    win[n] = 0.5f * (1.0f - (float)Math.Cos(2.0 * Math.PI * n / (copyFrames - 1)));
                                }
                            }
                            else if (copyFrames == 1)
                            {
                                win[0] = 1.0f;
                            }

                            for (int i = 0; i < copyFrames; i++)
                            {
                                int leftIndex = baseOffset + i;
                                int rightIndex = baseOffset + framesDim + i;
                                if (leftIndex < 0 || leftIndex >= flat.Length) continue;
                                float left = (float)(flat[leftIndex] - meanL);
                                float right = (rightIndex >= 0 && rightIndex < flat.Length) ? (float)(flat[rightIndex] - meanR) : left;
                                int frameIdx = startSample + processed + i;
                                if (frameIdx < 0 || frameIdx >= totalSamples) continue;
                                int targetPos = frameIdx * 2;
                                float w = win[i];
                                accum[targetPos] += left * w;
                                accum[targetPos + 1] += right * w;
                                weight[frameIdx] += w;
                            }

                            if (DebugMode) Console.WriteLine($"ONNX: chunk start={startSample + processed}, frames={copyFrames}, meanL={meanL:F8}, meanR={meanR:F8} (channels-first)");
                        }
                        else if (dimsChosen[3] == 2)
                        {
                            // channels-last: [1, stems, frames, 2]
                            int framesDim = dimsChosen[2];
                            int stemBlock = framesDim * 2;
                            int baseOffset = stemIdx * stemBlock;

                            double sumL = 0.0, sumR = 0.0;
                            for (int i = 0; i < copyFrames; i++)
                            {
                                int leftIndex = baseOffset + i * 2 + 0;
                                int rightIndex = baseOffset + i * 2 + 1;
                                if (leftIndex < 0 || leftIndex >= flat.Length) continue;
                                sumL += flat[leftIndex];
                                if (rightIndex >= 0 && rightIndex < flat.Length) sumR += flat[rightIndex];
                                else sumR += flat[leftIndex];
                            }
                            double meanL = copyFrames > 0 ? sumL / copyFrames : 0.0;
                            double meanR = copyFrames > 0 ? sumR / copyFrames : 0.0;

                            var win = new float[copyFrames];
                            if (copyFrames > 1)
                            {
                                for (int n = 0; n < copyFrames; n++)
                                {
                                    win[n] = 0.5f * (1.0f - (float)Math.Cos(2.0 * Math.PI * n / (copyFrames - 1)));
                                }
                            }
                            else if (copyFrames == 1)
                            {
                                win[0] = 1.0f;
                            }

                            for (int i = 0; i < copyFrames; i++)
                            {
                                int leftIndex = baseOffset + i * 2 + 0;
                                int rightIndex = baseOffset + i * 2 + 1;
                                if (leftIndex < 0 || leftIndex >= flat.Length) continue;
                                float left = (float)(flat[leftIndex] - meanL);
                                float right = (rightIndex >= 0 && rightIndex < flat.Length) ? (float)(flat[rightIndex] - meanR) : left;
                                int frameIdx = startSample + processed + i;
                                if (frameIdx < 0 || frameIdx >= totalSamples) continue;
                                int targetPos = frameIdx * 2;
                                float w = win[i];
                                accum[targetPos] += left * w;
                                accum[targetPos + 1] += right * w;
                                weight[frameIdx] += w;
                            }

                            if (DebugMode) Console.WriteLine($"ONNX: chunk start={startSample + processed}, frames={copyFrames}, meanL={meanL:F8}, meanR={meanR:F8} (channels-last)");
                        }
                    }
                    else
                    {
                        // Generic fallback (multi-dim heuristic)
                        double sumL = 0.0, sumR = 0.0;
                        int baseIndex = stemIdx * strides[stemAxis];
                        for (int i = 0; i < copyFrames; i++)
                        {
                            int leftIndex = baseIndex + 0 * strides[channelAxis] + i * strides[frameAxis];
                            int rightIndex = baseIndex + 1 * strides[channelAxis] + i * strides[frameAxis];
                            if (leftIndex < 0 || leftIndex >= flat.Length) continue;
                            sumL += flat[leftIndex];
                            if (rightIndex >= 0 && rightIndex < flat.Length) sumR += flat[rightIndex];
                            else sumR += flat[leftIndex];
                        }
                        double meanL = copyFrames > 0 ? sumL / copyFrames : 0.0;
                        double meanR = copyFrames > 0 ? sumR / copyFrames : 0.0;

                        // generic fallback: use Hann window and overlap-add
                        var win = new float[copyFrames];
                        if (copyFrames > 1)
                        {
                            for (int n = 0; n < copyFrames; n++)
                            {
                                win[n] = 0.5f * (1.0f - (float)Math.Cos(2.0 * Math.PI * n / (copyFrames - 1)));
                            }
                        }
                        else if (copyFrames == 1)
                        {
                            win[0] = 1.0f;
                        }

                        for (int i = 0; i < copyFrames; i++)
                        {
                            int leftIndex = baseIndex + 0 * strides[channelAxis] + i * strides[frameAxis];
                            int rightIndex = baseIndex + 1 * strides[channelAxis] + i * strides[frameAxis];
                            if (leftIndex < 0 || leftIndex >= flat.Length) continue;
                            float left = (float)(flat[leftIndex] - meanL);
                            float right = (rightIndex >= 0 && rightIndex < flat.Length) ? (float)(flat[rightIndex] - meanR) : left;
                            int frameIdx = startSample + processed + i;
                            if (frameIdx < 0 || frameIdx >= totalSamples) continue;
                            int targetPos = frameIdx * 2;
                            float w = win[i];
                            accum[targetPos] += left * w;
                            accum[targetPos + 1] += right * w;
                            weight[frameIdx] += w;
                        }

                        if (DebugMode) Console.WriteLine($"ONNX: chunk start={startSample + processed}, frames={copyFrames}, meanL={meanL:F8}, meanR={meanR:F8} (fallback)");
                    }

                    processed += sliceFrames;
                }
            }

            progress?.Report(1.0);

            // Finish overlap-add: normalize by weight buffer and write into finalResultInterleaved
            for (int f = 0; f < totalSamples; f++)
            {
                int tgt = f * 2;
                if (weight[f] > 0f)
                {
                    finalResultInterleaved[tgt] = accum[tgt] / weight[f];
                    finalResultInterleaved[tgt + 1] = accum[tgt + 1] / weight[f];
                }
                else
                {
                    // leave as zero
                    finalResultInterleaved[tgt] = 0f;
                    finalResultInterleaved[tgt + 1] = 0f;
                }
            }

            // Post-process: apply a safe normalization step to avoid clipping while
            // not amplifying noise. If the stem is very quiet, do not boost it.
            try
            {
                float peak = 0f;
                for (int i = 0; i < finalResultInterleaved.Length; i++)
                {
                    float v = Math.Abs(finalResultInterleaved[i]);
                    if (v > peak) peak = v;
                }

                // If too loud, gently attenuate to targetPeak
                const float targetPeak = 0.95f;
                if (peak > targetPeak && peak > 0f)
                {
                    float scale = targetPeak / peak;
                    for (int i = 0; i < finalResultInterleaved.Length; i++) finalResultInterleaved[i] *= scale;
                    Console.WriteLine($"ONNX: stem post-attenuated by {scale:F6} (peak {peak:F6})");
                }
                // If extremely quiet, do NOT amplify to avoid noise amplification
            }
            catch { }

            // Remove DC offset (small mean) introduced by model output if present.
            try
            {
                double sum = 0.0;
                int n = finalResultInterleaved.Length;
                for (int i = 0; i < n; i++) sum += finalResultInterleaved[i];
                double mean = n > 0 ? sum / n : 0.0;
                if (Math.Abs(mean) > 1e-5)
                {
                    for (int i = 0; i < n; i++) finalResultInterleaved[i] = (float)(finalResultInterleaved[i] - mean);
                    Console.WriteLine($"ONNX: subtracted DC offset {mean:F8} from stem output");
                }
            }
            catch { }

            return finalResultInterleaved;
        }

        private float[] PreparePlanarChunk(float[] interleavedData, int startFrame, int frameCount)
        {
            float[] planar = new float[frameCount * 2];
            for (int i = 0; i < frameCount; i++)
            {
                planar[i] = interleavedData[(startFrame + i) * 2];             // L
                planar[i + frameCount] = interleavedData[(startFrame + i) * 2 + 1]; // R
            }
            return planar;
        }

        private void FillInterleavedResult(float[] target, Tensor<float> outputTensor, int startFrame, int originalFrameCount)
        {
            var data = outputTensor.ToArray();
            int outputSamplesPerChannel = data.Length / 2;
            int copyFrames = Math.Min(originalFrameCount, outputSamplesPerChannel);
            for (int i = 0; i < copyFrames; i++)
            {
                target[(startFrame + i) * 2] = data[i];                      // L
                target[(startFrame + i) * 2 + 1] = data[i + outputSamplesPerChannel]; // R
            }
        }


        public Func<AudioObj, IProgress<double>, Task<float[]>> GetPartial(string stemName)
        {
            return (audio, progress) => this.ExtractStemAsync(audio, stemName, progress);
        }

        public void Dispose() => this._session?.Dispose();
    }
}