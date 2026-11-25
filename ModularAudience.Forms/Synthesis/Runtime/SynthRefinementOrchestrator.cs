using ModularAudience.Audio;
using ModularAudience.Audio.Synthesis;
using ModularAudience.Audio.Synthesis.Runtime;
using ModularAudience.Llama;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ModularAudience.Forms.Synthesis.Runtime;

/// <summary>
/// State of the refinement orchestrator state machine.
/// </summary>
public enum SynthRefinementState
{
    Idle,
    Rendering,
    AnalyzingWithLlm,
    Compiling,
    HotSwapping,
    Completed,
    Cancelled,
    Faulted
}

/// <summary>
/// A single iteration in the refinement history.
/// </summary>
public sealed class SynthRefinementIteration
{
    public int Index { get; set; }
    public SynthRefinementState State { get; set; }
    public string? Code { get; set; }
    public string? LlmCritique { get; set; }
    public string? CompileError { get; set; }
    public TimeSpan RenderTime { get; set; }
    public TimeSpan LlmLatency { get; set; }
    public TimeSpan CompileTime { get; set; }
    public float PeakAmplitude { get; set; }
    public bool IsAccepted { get; set; }
}

/// <summary>
/// Progress events raised by the orchestrator for the UI.
/// </summary>
public sealed class SynthRefinementProgress
{
    public SynthRefinementState State { get; init; }
    public int Iteration { get; init; }
    public int MaxIterations { get; init; }
    public string? Message { get; init; }
    public double Progress01 { get; init; }
}

/// <summary>
/// Orchestrates the LLM refinement loop: render -> critique -> compile -> hot-swap -> repeat.
/// Runs entirely on background threads; all UI marshalling is delegated to the caller via IProgress.
/// </summary>
public sealed class SynthRefinementOrchestrator : IDisposable
{
    private readonly LlamaConnector llm;
    private readonly SynthCompiler compiler;
    private readonly CustomInstrumentStore store;
    private readonly CustomInstrumentDescriptor descriptor;
    private readonly CancellationTokenSource externalCts;
    private readonly object stateGate = new();

    private SynthRefinementState state = SynthRefinementState.Idle;
    private int currentIteration;
    private int maxIterations = 10;
    private bool includeSpectrogram;
    public string UserPrompt { get; set; } = "";
    private IRuntimeProceduralSynth? activeSynth;
    private readonly List<SynthRefinementIteration> history = [];
    private bool disposed;
    private Task? loopTask;
    private CancellationTokenSource? loopCts;

    public event Action<SynthRefinementProgress>? ProgressChanged;
    public event Action<SynthRefinementIteration>? IterationCompleted;
    public event Action<IRuntimeProceduralSynth>? HotSwapped;
    public event Action<string>? CodeStreaming;

    public SynthRefinementState State
    {
        get
        {
            lock (this.stateGate) { return this.state; }
        }
        private set
        {
            lock (this.stateGate) { this.state = value; }
        }
    }

    public IReadOnlyList<SynthRefinementIteration> History => new ReadOnlyCollection<SynthRefinementIteration>(this.history);

    public IRuntimeProceduralSynth? ActiveSynth
    {
        get
        {
            lock (this.stateGate) { return this.activeSynth; }
        }
    }

    public SynthRefinementOrchestrator(
        LlamaConnector llm,
        SynthCompiler compiler,
        CustomInstrumentStore store,
        CustomInstrumentDescriptor descriptor)
    {
        this.llm = llm ?? throw new ArgumentNullException(nameof(llm));
        this.compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        this.externalCts = new CancellationTokenSource();
    }

    public int MaxIterations
    {
        get => this.maxIterations;
        set { if (value > 0) this.maxIterations = value; }
    }

    public bool IncludeSpectrogram
    {
        get => this.includeSpectrogram;
        set => this.includeSpectrogram = value;
    }

    /// <summary>
    /// When true, compile errors are sent back to the LLM for a retry in the same iteration.
    /// </summary>
    public bool AutoRetryCompileErrors { get; set; } = true;

    public void Start()
    {
        lock (this.stateGate)
        {
            if (this.state is SynthRefinementState.Rendering or SynthRefinementState.AnalyzingWithLlm
                or SynthRefinementState.Compiling or SynthRefinementState.HotSwapping)
            {
                return;
            }
            // Allow a fresh run after Completed, Cancelled, Faulted, or Idle.
            this.state = SynthRefinementState.Rendering;
            this.currentIteration = 0;
            this.loopCts = new CancellationTokenSource();
        }

        this.loopTask = Task.Run(() => RefinementLoopAsync(this.loopCts.Token));
    }

    public void Cancel()
    {
        lock (this.stateGate)
        {
            if (this.state == SynthRefinementState.Idle ||
                this.state == SynthRefinementState.Completed ||
                this.state == SynthRefinementState.Cancelled ||
                this.state == SynthRefinementState.Faulted)
            {
                return;
            }
            this.state = SynthRefinementState.Cancelled;
        }
        this.loopCts?.Cancel();
        this.externalCts.Cancel();
    }

    private async Task RefinementLoopAsync(CancellationToken token)
    {
        try
        {
            while (this.currentIteration < this.maxIterations)
            {
                token.ThrowIfCancellationRequested();

                var iterationState = SynthRefinementState.Idle;
                lock (this.stateGate) { iterationState = this.state; }
                if (iterationState == SynthRefinementState.Cancelled)
                {
                    break;
                }

                this.currentIteration++;
                int iterationNum = this.currentIteration;

                Report(SynthRefinementState.Rendering, iterationNum,
                    $"Rendering iteration {iterationNum} of {this.maxIterations}...", 0.1);

                // --- Phase 1: Render current code to audio ---
                var renderSw = System.Diagnostics.Stopwatch.StartNew();
                AudioObj? rendered = null;
                try
                {
                    rendered = await RenderCurrentAsync(token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    ReportFault($"Render failed: {ex.Message}");
                    return;
                }
                renderSw.Stop();

                if (rendered == null || rendered.Data.Length == 0)
                {
                    // If no code exists yet (first iteration), skip rendering and let LLM generate from scratch.
                    if (string.IsNullOrWhiteSpace(this.descriptor.Code))
                    {
                        // Generate silence buffer for the LLM to compare against reference.
                        int sr = this.descriptor.SampleRate;
                        int ch = this.descriptor.Channels;
                        int frames = (int)Math.Ceiling(GateSeconds() * sr);
                        rendered = new AudioObj
                        {
                            Data = new float[frames * ch],
                            SampleRate = sr,
                            Channels = ch,
                            BitDepth = 32,
                            Length = frames * ch,
                            Duration = TimeSpan.FromSeconds(frames / (double)sr),
                            Volume = 100
                        };
                        rendered.Rename(this.descriptor.Name);
                    }
                    else
                    {
                        ReportFault("Render produced empty audio.");
                        return;
                    }
                }

                float peak = 0f;
                foreach (float s in rendered.Data) { peak = Math.Max(peak, Math.Abs(s)); }

                var iterationRecord = new SynthRefinementIteration
                {
                    Index = iterationNum,
                    State = SynthRefinementState.Rendering,
                    Code = this.descriptor.Code,
                    RenderTime = renderSw.Elapsed,
                    PeakAmplitude = peak
                };

                // --- Phase 2: Analyze with LLM ---
                Report(SynthRefinementState.AnalyzingWithLlm, iterationNum,
                    "Asking LLM for critique and improved DSP code...", 0.4);

                byte[] referenceWav = await LoadReferenceWavAsync(token);
                byte[] synthesizedWav = AudioObjToWavBytes(rendered);
                byte[]? spectrogramPng = this.includeSpectrogram ? GenerateSpectrogramPng(rendered) : null;

                var llmSw = System.Diagnostics.Stopwatch.StartNew();
                string? llmResponse;
                try
                {
                    var accumulated = new StringBuilder();
                    int chunkCount = 0;
                    llmResponse = await this.llm.SendMultimodalSynthPromptStreamingAsync(
                        referenceWav, synthesizedWav, spectrogramPng,
                        BuildSynthPrompt(iterationNum), this.includeSpectrogram,
                        chunk =>
                        {
                            accumulated.Append(chunk);
                            chunkCount++;
                            if (chunkCount == 1)
                            {
                                Report(SynthRefinementState.AnalyzingWithLlm, iterationNum,
                                    $"First LLM stream chunk received ({accumulated.Length} characters).", 0.45);
                            }
                            else if (chunkCount % 20 == 0)
                            {
                                Report(SynthRefinementState.AnalyzingWithLlm, iterationNum,
                                    $"LLM stream chunk {chunkCount}: {accumulated.Length} characters accumulated.", 0.45);
                            }
                            CodeStreaming?.Invoke(accumulated.ToString());
                        });
                    if (llmResponse == null && accumulated.Length > 0)
                    {
                        llmResponse = accumulated.ToString();
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    llmResponse = null;
                    Report(SynthRefinementState.AnalyzingWithLlm, iterationNum, $"LLM call failed: {ex.Message}", 0.4, isWarning: true);
                }
                llmSw.Stop();

                if (string.IsNullOrEmpty(llmResponse))
                {
                    Report(SynthRefinementState.AnalyzingWithLlm, iterationNum,
                        "LLM returned no text; keeping the previous code visible.", 0.5, isWarning: true);
                }
                else
                {
                    Report(SynthRefinementState.AnalyzingWithLlm, iterationNum,
                        $"LLM response received: {llmResponse.Length} raw characters in {llmSw.Elapsed.TotalSeconds:0.##}s.", 0.5);
                }

                iterationRecord.LlmLatency = llmSw.Elapsed;
                iterationRecord.LlmCritique = llmResponse;

                // --- Phase 3: Extract code from LLM response and compile ---
                // Show the exact LLM output immediately, even when it is not compilable yet.
                string newCode = NormalizeGeneratedCode(llmResponse);
                Report(SynthRefinementState.Compiling, iterationNum,
                    $"Extracted {newCode.Length} characters of C# code from the LLM response.", 0.6);
                if (newCode.Length > 0)
                {
                    PublishCode(newCode, iterationRecord);
                    Report(SynthRefinementState.Compiling, iterationNum,
                        "Showing latest LLM code in editor before compilation.", 0.65);
                }
                else
                {
                    Report(SynthRefinementState.Compiling, iterationNum,
                        "No C# code extracted; leaving the previous editor content unchanged.", 0.6, isWarning: true);
                }

                Report(SynthRefinementState.Compiling, iterationNum,
                    "Compiling improved DSP code...", 0.7);

                var compileSw = System.Diagnostics.Stopwatch.StartNew();
                CompileResult compileResult;
                try
                {
                    compileResult = this.compiler.Compile(newCode, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    compileResult = CompileResult.Failed($"Compiler exception: {ex.Message}");
                }
                compileSw.Stop();

                iterationRecord.CompileTime = compileSw.Elapsed;

                if (!compileResult.IsSuccess)
                {
                    iterationRecord.CompileError = string.Join(" | ", compileResult.Errors);
                    Report(SynthRefinementState.Compiling, iterationNum,
                        $"Compile failed: {iterationRecord.CompileError}", 0.7);
                    Report(SynthRefinementState.Compiling, iterationNum,
                        "Showing latest LLM code in editor even though compilation failed.", 0.7, isWarning: true);
                    IterationCompleted?.Invoke(iterationRecord);

                    if (this.AutoRetryCompileErrors)
                    {
                        // Self-healing: stream the retry too, so the code box never stays empty.
                        Report(SynthRefinementState.Compiling, iterationNum,
                            "Compile error auto-retry enabled; sending errors back to LLM.", 0.72);
                        int retryChunkCount = 0;
                        string? retryCode = await RetryWithCompileErrorAsync(
                            newCode, iterationRecord.CompileError, referenceWav, synthesizedWav, spectrogramPng,
                            chunk =>
                            {
                                retryChunkCount++;
                                if (retryChunkCount == 1)
                                {
                                    Report(SynthRefinementState.Compiling, iterationNum,
                                        $"First retry stream chunk received ({chunk.Length} characters).", 0.75);
                                }
                                else if (retryChunkCount % 20 == 0)
                                {
                                    Report(SynthRefinementState.Compiling, iterationNum,
                                        $"Retry stream chunk {retryChunkCount}: {chunk.Length} characters accumulated.", 0.75);
                                }
                                CodeStreaming?.Invoke(chunk);
                            }, token);

                        Report(SynthRefinementState.Compiling, iterationNum,
                            $"Retry response received: {retryCode?.Length ?? 0} characters.", 0.78);
                        string normalizedRetry = NormalizeGeneratedCode(retryCode);
                        if (normalizedRetry.Length > 0)
                        {
                            newCode = normalizedRetry;
                            PublishCode(newCode, iterationRecord);
                            Report(SynthRefinementState.Compiling, iterationNum,
                                $"Showing retry code in editor ({newCode.Length} characters).", 0.8);
                            try
                            {
                                compileResult = this.compiler.Compile(newCode, token);
                            }
                            catch (OperationCanceledException) { break; }
                            catch (Exception ex)
                            {
                                compileResult = CompileResult.Failed($"Compiler exception: {ex.Message}");
                            }
                            iterationRecord.CompileError = compileResult.IsSuccess
                                ? null
                                : string.Join(" | ", compileResult.Errors);
                            if (!compileResult.IsSuccess)
                            {
                                Report(SynthRefinementState.Compiling, iterationNum,
                                    $"Retry compile failed: {iterationRecord.CompileError}", 0.8, isWarning: true);
                            }
                        }
                        else
                        {
                            Report(SynthRefinementState.Compiling, iterationNum,
                                "Retry returned no code; keeping the previous LLM code visible.", 0.78, isWarning: true);
                        }
                    }
                    else
                    {
                        Report(SynthRefinementState.Compiling, iterationNum,
                            "Compile error auto-retry is disabled.", 0.7, isWarning: true);
                    }

                    if (!compileResult.IsSuccess)
                    {
                        // Keep the last LLM code visible even though the loop faults here.
                        PublishCode(newCode, iterationRecord);
                        ReportFault($"Compilation failed: {iterationRecord.CompileError}");
                        return;
                    }
                }

                // --- Phase 4: Hot-swap into live playback ---
                Report(SynthRefinementState.HotSwapping, iterationNum,
                    "Hot-swapping into live synth slot...", 0.9);

                try
                {
                    await HotSwapAsync(compileResult.Assembly!, newCode, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    ReportFault($"Hot-swap failed: {ex.Message}");
                    return;
                }

                PublishCode(newCode, iterationRecord);
                this.store.Save(this.descriptor);

                iterationRecord.State = SynthRefinementState.HotSwapping;
                IterationCompleted?.Invoke(iterationRecord);

                // Check if we should continue refining or stop
                if (iterationNum >= this.maxIterations)
                {
                    break;
                }
            }

            lock (this.stateGate)
            {
                if (this.state != SynthRefinementState.Cancelled &&
                    this.state != SynthRefinementState.Faulted)
                {
                    this.state = SynthRefinementState.Completed;
                }
            }
            if (this.State != SynthRefinementState.Cancelled &&
                this.State != SynthRefinementState.Faulted)
            {
                ProgressChanged?.Invoke(new SynthRefinementProgress
                {
                    State = SynthRefinementState.Completed,
                    Iteration = this.currentIteration,
                    MaxIterations = this.maxIterations,
                    Message = "Refinement complete.",
                    Progress01 = 1.0
                });
            }
        }
        catch (OperationCanceledException)
        {
            lock (this.stateGate) { this.state = SynthRefinementState.Cancelled; }
            ProgressChanged?.Invoke(new SynthRefinementProgress
            {
                State = SynthRefinementState.Cancelled,
                Iteration = this.currentIteration,
                MaxIterations = this.maxIterations,
                Message = "Cancelled.",
                Progress01 = 1.0
            });
        }
        catch (Exception ex)
        {
            ReportFault($"Orchestrator fault: {ex.Message}");
        }
    }

    private double GateSeconds() => 60.0 / Math.Max(1.0, this.descriptor.Bpm) * this.descriptor.NoteBeats;

    private async Task<AudioObj> RenderCurrentAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(this.descriptor.Code))
        {
            // No code yet - return silence for the first LLM generation.
            int sr = this.descriptor.SampleRate;
            int ch = this.descriptor.Channels;
            int frames = (int)Math.Ceiling(GateSeconds() * sr);
            var silence = new AudioObj
            {
                Data = new float[frames * ch],
                SampleRate = sr,
                Channels = ch,
                BitDepth = 32,
                Length = frames * ch,
                Duration = TimeSpan.FromSeconds(frames / (double)sr),
                Volume = 100
            };
            silence.Rename(this.descriptor.Name);
            return silence;
        }

        if (this.activeSynth != null)
        {
            int sampleRate = this.activeSynth.SampleRate;
            int channels = this.activeSynth.Channels;
            int frames = (int)Math.Ceiling(GateSeconds() * sampleRate);
            var buffer = new float[frames * channels];
            int written = this.activeSynth.Render(buffer, frames, token);
            var audio = new AudioObj
            {
                Data = buffer[..written],
                SampleRate = sampleRate,
                Channels = channels,
                BitDepth = 32,
                Length = written,
                Duration = TimeSpan.FromSeconds(frames / (double)sampleRate),
                Volume = 100
            };
            audio.Rename(this.descriptor.Name);
            return audio;
        }

        // Compile the current code on-the-fly for rendering
        var compileResult = this.compiler.Compile(this.descriptor.Code, token);
        if (!compileResult.IsSuccess)
        {
            throw new InvalidOperationException($"Cannot render: {string.Join(" | ", compileResult.Errors)}");
        }

        var synth = new CustomInstrumentSynth(this.descriptor, compileResult.Assembly!);
        var safeSynth = new SynthSandboxDecorator(synth);
        int synthRate = safeSynth.SampleRate;
        int synthChannels = safeSynth.Channels;
        int synthFrames = (int)Math.Ceiling(GateSeconds() * synthRate);
        var synthBuffer = new float[synthFrames * synthChannels];
        int synthWritten = safeSynth.Render(synthBuffer, synthFrames, token);
        var synthAudio = new AudioObj
        {
            Data = synthBuffer[..synthWritten],
            SampleRate = synthRate,
            Channels = synthChannels,
            BitDepth = 32,
            Length = synthWritten,
            Duration = TimeSpan.FromSeconds(synthFrames / (double)synthRate),
            Volume = 100
        };
        synthAudio.Rename(this.descriptor.Name);
        return synthAudio;
    }

    private async Task HotSwapAsync(Assembly assembly, string code, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var synth = new CustomInstrumentSynth(this.descriptor, assembly);
        var safeSynth = new SynthSandboxDecorator(synth);

        lock (this.stateGate)
        {
            var previous = this.activeSynth;
            this.activeSynth = safeSynth;
            _ = previous; // old instance will be GC'd
        }

        HotSwapped?.Invoke(safeSynth);
    }

    private async Task<string?> RetryWithCompileErrorAsync(
        string failedCode,
        string compileError,
        byte[] referenceWav,
        byte[] synthesizedWav,
        byte[]? spectrogramPng,
        Action<string>? onChunk,
        CancellationToken token)
    {
        string retryPrompt = $@"The following C# DSP code failed to compile with these errors:

{compileError}

Here is the code that failed:

{failedCode}

Keep this exact contract:
{SynthContract.RequiredUsings}
{SynthContract.ExpectedSignature}
DTO shape (compact reference only, do NOT redeclare it): {SynthContract.DtoShape}

Please fix the compilation errors and return only the corrected C# code in a ```csharp block. Do not change the algorithm logic, only fix syntax/type errors so it compiles under the restricted sandbox (no System.IO, System.Net, System.Reflection, DllImport, unsafe).";

        var accumulated = new StringBuilder();
        string? retryResponse;
        try
        {
            retryResponse = await this.llm.SendMultimodalSynthPromptStreamingAsync(
                referenceWav, synthesizedWav, spectrogramPng, retryPrompt, this.includeSpectrogram,
                chunk =>
                {
                    accumulated.Append(chunk);
                    onChunk?.Invoke(accumulated.ToString());
                });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return null;
        }

        return retryResponse ?? (accumulated.Length > 0 ? accumulated.ToString() : null);
    }

    private byte[] AudioObjToWavBytes(AudioObj audio)
    {
        using var ms = new MemoryStream();
        using var writer = new NAudio.Wave.WaveFileWriter(ms, new NAudio.Wave.WaveFormat(audio.SampleRate, audio.Channels));
        for (int i = 0; i < audio.Data.Length; i++)
        {
            writer.WriteSample(audio.Data[i]);
        }
        writer.Flush();
        return ms.ToArray();
    }

    private byte[] GenerateSpectrogramPng(AudioObj audio)
    {
        int width = 512;
        int height = 256;
        using var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Black);

        int sampleRate = audio.SampleRate;
        int channels = audio.Channels;
        int frameSize = 1024;
        int hopSize = 512;
        int frames = Math.Max(1, (audio.Data.Length / channels - frameSize) / hopSize + 1);
        frames = Math.Min(frames, width);

        for (int x = 0; x < frames; x++)
        {
            int start = x * hopSize * channels;
            var window = new System.Numerics.Complex[frameSize];
            for (int i = 0; i < frameSize; i++)
            {
                if (start + i * channels < audio.Data.Length)
                {
                    float sample = audio.Data[start + i * channels];
                    double hann = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (frameSize - 1)));
                    window[i] = new System.Numerics.Complex(sample * hann, 0);
                }
            }

            MathNet.Numerics.IntegralTransforms.Fourier.Forward(window, MathNet.Numerics.IntegralTransforms.FourierOptions.Matlab);

            for (int y = 0; y < height; y++)
            {
                int bin = y * frameSize / (2 * height);
                if (bin < frameSize / 2)
                {
                    double mag = window[bin].Magnitude;
                    double db = 20 * Math.Log10(Math.Max(mag, 1e-10));
                    double normalized = Math.Clamp((db + 80) / 80, 0, 1);
                    int intensity = (int)(normalized * 255);
                    bitmap.SetPixel(x, height - 1 - y, Color.FromArgb(intensity, intensity, 255));
                }
            }
        }

        using var outMs = new MemoryStream();
        bitmap.Save(outMs, ImageFormat.Png);
        return outMs.ToArray();
    }

    private async Task<byte[]> LoadReferenceWavAsync(CancellationToken token)
    {
        // Generate a simple reference tone (sine wave at the target note)
        int sampleRate = this.descriptor.SampleRate;
        int channels = this.descriptor.Channels;
        double frequency = 440.0 * Math.Pow(2.0, (this.descriptor.MidiNote - 69) / 12.0);
        int durationSeconds = 2;
        int frames = durationSeconds * sampleRate;
        using var ms = new MemoryStream();
        using var writer = new NAudio.Wave.WaveFileWriter(ms, new NAudio.Wave.WaveFormat(sampleRate, channels));
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)sampleRate;
            float sample = (float)(0.3 * Math.Sin(2 * Math.PI * frequency * t));
            for (int c = 0; c < channels; c++) { writer.WriteSample(sample); }
        }
        writer.Flush();
        return ms.ToArray();
    }

    private string BuildSynthPrompt(int iteration)
    {
        return $@"You are an expert C# DSP engineer. Improve the following procedural instrument synth code to better match the reference audio.

Current iteration: {iteration} of {this.maxIterations}.
Instrument: {this.descriptor.Name}
Description: {this.descriptor.Description}
Sample rate: {this.descriptor.SampleRate} Hz, Channels: {this.descriptor.Channels}
MIDI note: {this.descriptor.MidiNote}, BPM: {this.descriptor.Bpm}, Note beats: {this.descriptor.NoteBeats}

User refinement request: {this.UserPrompt}

Current C# code (empty on first iteration - generate from scratch):
```csharp
{this.descriptor.Code}
```

Listen to the reference audio (input_audio[0]) and{(string.IsNullOrWhiteSpace(this.descriptor.Code) ? " generate a matching instrument from scratch" : " the current synthesized output (input_audio[1])")}{(this.includeSpectrogram ? ", and review the spectrogram image (image_url)" : "")}.

Return {(string.IsNullOrWhiteSpace(this.descriptor.Code) ? "new" : "improved")} C# code in a ```csharp code block. The code must:
- Start with both required usings: {SynthContract.RequiredUsings}
- Define a public class named CustomSynth with exactly: {SynthContract.ExpectedSignature}
- Use the host-provided SynthRenderContext; do NOT declare it yourself
- DTO shape (compact reference only): {SynthContract.DtoShape}
- Access parameters via ctx.SampleRate, ctx.Channels, ctx.FrameCount, ctx.NoteFrequencyHz, ctx.Bpm, ctx.NoteBeats, ctx.GateSeconds, ctx.MidiNote, ctx.GainPercent, ctx.PitchSemitones
- Return interleaved float32 PCM in [-1, 1] (array length = ctx.FrameCount * ctx.Channels)
- NOT use System.IO, System.Net, System.Reflection, DllImport, unsafe, or any process/file/network APIs
- Only use Math, System.Numerics, and basic arithmetic; Math is available through the required using
- Be a complete, compilable C# file with the class and method

Exact method header: {SynthContract.ExpectedSignature}
Use Math for trigonometry and envelopes.

Focus on timbre, envelope, and spectral match to the reference. Return ONLY the code block, no explanation.";
    }

    private void PublishCode(string code, SynthRefinementIteration iteration)
    {
        if (string.IsNullOrWhiteSpace(code)) return;

        iteration.Code = code;
        this.descriptor.Code = code;
        this.descriptor.UpdatedAt = DateTime.UtcNow;
        CodeStreaming?.Invoke(code);
    }

    private static string NormalizeGeneratedCode(string? response)
    {
        string code = (ExtractCodeBlock(response) ?? response ?? string.Empty).Trim();
        if (code.Length == 0) return string.Empty;

        // The model sometimes omits usings. Add only what is missing so the code box shows
        // exactly the source that is handed to the compiler.
        if (!Regex.IsMatch(code, @"(?m)^\s*using\s+System\s*;"))
        {
            code = SynthContract.RequiredSystemUsing + Environment.NewLine + code;
        }
        if (!Regex.IsMatch(code, @"(?m)^\s*using\s+ModularAudience\.Audio\.Synthesis\.Runtime\s*;"))
        {
            code = SynthContract.RequiredUsing + Environment.NewLine + code;
        }

        return code;
    }

    private static string? ExtractCodeBlock(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        var match = Regex.Match(response, @"```csharp\s*\r?\n(.*?)(?:\r?\n)?```", RegexOptions.Singleline);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        match = Regex.Match(response, @"```\s*\r?\n(.*?)(?:\r?\n)?```", RegexOptions.Singleline);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // Fallback: strip backtick lines
        if (response.Contains("```"))
        {
            var codeLines = response.Split('\n').Where(l => !l.TrimStart().StartsWith("```")).ToArray();
            if (codeLines.Length > 0)
            {
                return string.Join("\n", codeLines).Trim();
            }
        }

        return null;
    }

    private void Report(SynthRefinementState state, int iteration, string? message, double progress01, bool isWarning = false)
    {
        lock (this.stateGate)
        {
            // Don't overwrite Cancelled state.
            if (this.state == SynthRefinementState.Cancelled) return;
            this.state = state;
        }
        ProgressChanged?.Invoke(new SynthRefinementProgress
        {
            State = state,
            Iteration = iteration,
            MaxIterations = this.maxIterations,
            Message = isWarning ? $"Warning: {message}" : message,
            Progress01 = progress01
        });
    }

    private void ReportFault(string message)
    {
        lock (this.stateGate)
        {
            if (this.state == SynthRefinementState.Cancelled) return;
            this.state = SynthRefinementState.Faulted;
        }
        if (this.State == SynthRefinementState.Cancelled) return;
        ProgressChanged?.Invoke(new SynthRefinementProgress
        {
            State = SynthRefinementState.Faulted,
            Iteration = this.currentIteration,
            MaxIterations = this.maxIterations,
            Message = message,
            Progress01 = 1.0
        });
    }

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            Cancel();
            this.loopCts?.Dispose();
            this.externalCts.Dispose();
            this.compiler.Dispose();
        }
    }
}
