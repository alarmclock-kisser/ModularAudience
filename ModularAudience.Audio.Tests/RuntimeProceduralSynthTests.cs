using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio;
using ModularAudience.Audio.Synthesis;
using ModularAudience.Audio.Synthesis.Runtime;
using ModularAudience.Forms.Synthesis.Runtime;
using ModularAudience.Llama;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Tests;

/// <summary>
/// Tests for the Runtime Procedural Instrument Synthesizer.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RuntimeProceduralSynthTests
{
    private string storageDirectory = "";
    private CustomInstrumentStore store = null!;

    [TestInitialize]
    public void InitializeStore()
    {
        storageDirectory = Path.Combine(Path.GetTempPath(), "custom-synth-tests-" + Guid.NewGuid().ToString("N"));
        store = new CustomInstrumentStore(storageDirectory);
    }

    [TestCleanup]
    public void CleanupStore()
    {
        if (Directory.Exists(storageDirectory))
        {
            Directory.Delete(storageDirectory, true);
        }
    }

    // ---- Conservative display formatter ----

    [TestMethod]
    public void Formatter_InsertsOnlyMissingSemicolonNewlines_AndKeepsModelIndentation()
    {
        string collapsed =
            "using System;using ModularAudience.Audio.Synthesis.Runtime;public class CustomSynth" +
            "{public static float[] Render(SynthRenderContext ctx)" +
            "{var data = new float[ctx.FrameCount * ctx.Channels];        data[0] = 1f;" +
            "        data[1] = 2f;    return data;}}";

        string formatted = CSharpCodeFormatter.SmartFormat(collapsed);

        Assert.IsTrue(formatted.Contains("using System;\nusing ModularAudience"), "Missing newline after using must be inserted.");
        Assert.IsTrue(formatted.Contains(";\n        data[0] = 1f;"), "Model-provided indentation must be preserved verbatim.");
        Assert.IsTrue(formatted.Contains(";\n    return data;"), "Existing horizontal whitespace before a statement must remain unchanged.");
        Assert.IsTrue(formatted.Contains("data[0] = 1f;\n        data[1] = 2f;"), "Missing newline after a statement must be inserted.");
    }

    [TestMethod]
    public void Formatter_LeavesProperlyFormattedCodeAndSemicolonsInStringsUntouched()
    {
        string formattedCode =
            "using System;\nusing ModularAudience.Audio.Synthesis.Runtime;\n" +
            "public class CustomSynth\n{\n    public static float[] Render(SynthRenderContext ctx)\n    {\n" +
            "        var a = 1;\n        var b = 2;\n        var c = 3;\n        var d = 4;\n        var e = 5;\n" +
            "        return new float[] { a, b, c, d, e };\n    }\n}";
        Assert.AreEqual(formattedCode, CSharpCodeFormatter.SmartFormat(formattedCode));

        string collapsedWithString =
            "using System;using ModularAudience.Audio.Synthesis.Runtime;public class CustomSynth" +
            "{public static float[] Render(SynthRenderContext ctx){string s=\";\";" +
            "var a=1;var b=2;var c=3;var d=4;var e=5;return new float[]{a,b,c,d,e};}}";

        string result = CSharpCodeFormatter.SmartFormat(collapsedWithString);
        Assert.IsTrue(result.Contains("string s=\";\";\n"), "Semicolon inside a string literal must not be treated as a statement terminator.");
        Assert.IsFalse(result.Contains("string s=\";\n"), "String literal must stay intact.");
    }

    // ---- Numeric control propagation ----

    [TestMethod]
    public void SampleSynthSettings_AreAppliedToCustomDescriptorWithoutSaving()
    {
        var settings = new InstrumentSynthSettings
        {
            SampleRate = 32000, Channels = 2, MidiNote = 64, PitchSemitones = 3.5,
            Bpm = 90, NoteBeats = 2, DurationMultiplier = 1.25, GainPercent = 42,
            AttackMs = 11, DecaySeconds = 1.75, SustainPercent = 65, ReleaseSeconds = 0.4,
            Brightness = 77, Body = 33, Noise = 19, Drive = 8, DetuneCents = 6,
            VibratoCents = 12, VibratoHz = 4, CutoffHz = 9500, Resonance = 1.4,
            FilterEnvelope = 55, RoomPercent = 21, StereoWidth = 48, Seed = 99
        };
        var descriptor = new CustomInstrumentDescriptor("MappingProbe", "");

        var map = typeof(InstrumentSampleSynth).GetMethod(
            "ApplySettingsToCustomDescriptor",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(map);
        map.Invoke(null, [settings, descriptor]);

        Assert.AreEqual(settings.SampleRate, descriptor.SampleRate);
        Assert.AreEqual(settings.Channels, descriptor.Channels);
        Assert.AreEqual(settings.MidiNote, descriptor.MidiNote);
        Assert.AreEqual(settings.PitchSemitones, descriptor.PitchSemitones);
        Assert.AreEqual(settings.Bpm, descriptor.Bpm);
        Assert.AreEqual(settings.NoteBeats, descriptor.NoteBeats);
        Assert.AreEqual(settings.DurationMultiplier, descriptor.DurationMultiplier);
        Assert.AreEqual(settings.GainPercent, descriptor.GainPercent);
        Assert.AreEqual(settings.AttackMs, descriptor.AttackMs);
        Assert.AreEqual(settings.DecaySeconds, descriptor.DecaySeconds);
        Assert.AreEqual(settings.SustainPercent, descriptor.SustainPercent);
        Assert.AreEqual(settings.ReleaseSeconds, descriptor.ReleaseSeconds);
        Assert.AreEqual(settings.Brightness, descriptor.Brightness);
        Assert.AreEqual(settings.Body, descriptor.Body);
        Assert.AreEqual(settings.Noise, descriptor.Noise);
        Assert.AreEqual(settings.Drive, descriptor.Drive);
        Assert.AreEqual(settings.DetuneCents, descriptor.DetuneCents);
        Assert.AreEqual(settings.VibratoCents, descriptor.VibratoCents);
        Assert.AreEqual(settings.VibratoHz, descriptor.VibratoHz);
        Assert.AreEqual(settings.CutoffHz, descriptor.CutoffHz);
        Assert.AreEqual(settings.Resonance, descriptor.Resonance);
        Assert.AreEqual(settings.FilterEnvelope, descriptor.FilterEnvelope);
        Assert.AreEqual(settings.RoomPercent, descriptor.RoomPercent);
        Assert.AreEqual(settings.StereoWidth, descriptor.StereoWidth);
        Assert.AreEqual(settings.Seed, descriptor.Seed);
    }

    // ---- Roslyn compile success ----

    [TestMethod]
    public void SynthCompiler_ValidCode_CompilesSuccessfully()
    {
        var compiler = new SynthCompiler();
        string code = @"using System;
using ModularAudience.Audio.Synthesis.Runtime;
public class CustomSynth
{
    public static float[] Render(SynthRenderContext ctx)
    {
        var data = new float[ctx.FrameCount * ctx.Channels];
        for (int i = 0; i < ctx.FrameCount; i++)
        {
            double t = i / (double)ctx.SampleRate;
            float s = (float)(0.5 * Math.Sin(2 * Math.PI * ctx.NoteFrequencyHz * t));
            for (int c = 0; c < ctx.Channels; c++) data[i * ctx.Channels + c] = s;
        }
        return data;
    }
}";
        var result = compiler.Compile(code);
        Assert.IsTrue(result.IsSuccess, "Valid code should compile successfully.");
        Assert.IsNotNull(result.Assembly, "Compiled assembly should not be null.");

        var descriptor = new CustomInstrumentDescriptor("TestSynth", code) { SampleRate = 44100, Channels = 2, MidiNote = 69 };
        var synth = new CustomInstrumentSynth(descriptor, result.Assembly);
        var buffer = new float[44100 * 2];
        int written = synth.Render(buffer, 44100);
        Assert.AreEqual(44100 * 2, written, "Should render all requested samples.");
        Assert.IsTrue(buffer.Any(v => v != 0f), "Rendered audio should not be silence.");

        compiler.Dispose();
    }

    // ---- Roslyn compile failure ----

    [TestMethod]
    public void SynthCompiler_InvalidCode_ReturnsErrors()
    {
        var compiler = new SynthCompiler();
        string code = @"using System;
public class CustomSynth
{
    public static float[] Render(SynthRenderContext ctx)
    {
        this is invalid C# syntax !!!;
        return new float[0];
    }
}";
        var result = compiler.Compile(code);
        Assert.IsFalse(result.IsSuccess, "Invalid code should fail to compile.");
        Assert.IsTrue(result.Errors.Length > 0, "Should return compile errors.");

        compiler.Dispose();
    }

    [TestMethod]
    public void CustomInstrumentSynth_WrongSignature_ErrorIncludesContract()
    {
        var compiler = new SynthCompiler();
        string code = @"using System;
public class CustomSynth
{
    public static float[] Render(int sampleRate, int channels, int frameCount, double noteFrequencyHz)
    {
        return new float[0];
    }
}";
        var result = compiler.Compile(code);
        Assert.IsTrue(result.IsSuccess, "Wrong-signature code must still compile.");

        var descriptor = new CustomInstrumentDescriptor("WrongSignature", code);
        InvalidOperationException? ex = null;
        try
        {
            _ = new CustomInstrumentSynth(descriptor, result.Assembly!);
        }
        catch (InvalidOperationException caught)
        {
            ex = caught;
        }
        Assert.IsNotNull(ex, "A wrong Render signature must be rejected.");

        StringAssert.Contains(ex!.Message, "public static float[] Render(SynthRenderContext ctx)");
        StringAssert.Contains(ex.Message, "SampleRate");
        StringAssert.Contains(ex.Message, "PitchSemitones");
        StringAssert.Contains(ex.Message, "do NOT redeclare it");

        compiler.Dispose();
    }

    // ---- Forbidden API rejection ----

    [TestMethod]
    [DataRow("System.IO.File.ReadAllBytes(\"test\")")]
    [DataRow("System.Net.Http.HttpClient")]
    [DataRow("System.Reflection.Assembly.Load")]
    [DataRow("DllImport")]
    [DataRow("unsafe")]
    [DataRow("Process.Start")]
    public void SynthCompiler_ForbiddenApi_Rejected(string forbiddenSnippet)
    {
        var compiler = new SynthCompiler();
        string code = $@"using System;
public class CustomSynth
{{
    public static float[] Render(SynthRenderContext ctx)
    {{
        {forbiddenSnippet};
        return new float[ctx.FrameCount * ctx.Channels];
    }}
}}";
        var result = compiler.Compile(code);
        Assert.IsFalse(result.IsSuccess, $"Code with '{forbiddenSnippet}' should be rejected.");
        Assert.IsTrue(result.Errors.Length > 0, "Should return rejection reason.");

        compiler.Dispose();
    }

    // ---- AssemblyLoadContext unload via WeakReference + GC ----

    [TestMethod]
    public void SynthCompiler_Unload_CollectsAssembly()
    {
        WeakReference assemblyRef = CreateAndUnloadCompiler(out bool compileSuccess);
        Assert.IsTrue(compileSuccess, "Code should compile successfully.");

        for (int i = 0; i < 20; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            if (!assemblyRef.IsAlive) break;
        }

        Assert.IsFalse(assemblyRef.IsAlive, "Assembly should be collected after unload.");
    }

    private static WeakReference CreateAndUnloadCompiler(out bool compileSuccess)
    {
        var compiler = new SynthCompiler();
        string code = @"using System;
using ModularAudience.Audio.Synthesis.Runtime;
public class CustomSynth
{
    public static float[] Render(SynthRenderContext ctx)
    {
        return new float[ctx.FrameCount * ctx.Channels];
    }
}";
        var result = compiler.Compile(code);
        compileSuccess = result.IsSuccess;
        var assembly = result.Assembly;
        var assemblyRef = new WeakReference(assembly!);
        result = null;
        assembly = null;
        compiler.Unload();
        compiler.Dispose();
        return assemblyRef;
    }

    // ---- Safety decorator clamping ----

    [TestMethod]
    public void SynthSandboxDecorator_ClampsNanAndInfinity()
    {
        var descriptor = new CustomInstrumentDescriptor("NanSynth", "") { SampleRate = 44100, Channels = 1, MidiNote = 60 };
        var compiler = new SynthCompiler();
        string code = @"using System;
using ModularAudience.Audio.Synthesis.Runtime;
public class CustomSynth
{
    public static float[] Render(SynthRenderContext ctx)
    {
        var data = new float[ctx.FrameCount * ctx.Channels];
        data[0] = float.NaN;
        data[1] = float.PositiveInfinity;
        data[2] = float.NegativeInfinity;
        data[3] = 5.0f;
        data[4] = -5.0f;
        return data;
    }
}";
        var result = compiler.Compile(code);
        Assert.IsTrue(result.IsSuccess);

        var rawSynth = new CustomInstrumentSynth(descriptor, result.Assembly);
        var safeSynth = new SynthSandboxDecorator(rawSynth);

        var buffer = new float[5];
        int written = safeSynth.Render(buffer, 5);
        Assert.AreEqual(5, written);

        foreach (float s in buffer)
        {
            Assert.IsTrue(float.IsFinite(s), "All output samples must be finite.");
            Assert.IsTrue(s >= -1.0f && s <= 1.0f, $"Sample {s} must be in [-1, 1].");
        }

        compiler.Dispose();
    }

    // ---- WAV header validity ----

    [TestMethod]
    public void WavHeader_ValidAfterRender()
    {
        var descriptor = new CustomInstrumentDescriptor("WavTest", "") { SampleRate = 44100, Channels = 2, MidiNote = 60 };
        var compiler = new SynthCompiler();
        string code = @"using System;
using ModularAudience.Audio.Synthesis.Runtime;
public class CustomSynth
{
    public static float[] Render(SynthRenderContext ctx)
    {
        var data = new float[ctx.FrameCount * ctx.Channels];
        for (int i = 0; i < data.Length; i++) data[i] = 0.5f;
        return data;
    }
}";
        var result = compiler.Compile(code);
        Assert.IsTrue(result.IsSuccess);

        var synth = new CustomInstrumentSynth(descriptor, result.Assembly);
        var safeSynth = new SynthSandboxDecorator(synth);

        int frames = 1000;
        var buffer = new float[frames * 2];
        safeSynth.Render(buffer, frames);

        using var ms = new MemoryStream();
        using var writer = new NAudio.Wave.WaveFileWriter(ms, new NAudio.Wave.WaveFormat(44100, 2));
        for (int i = 0; i < buffer.Length; i++) writer.WriteSample(buffer[i]);
        writer.Flush();

        byte[] wavBytes = ms.ToArray();
        Assert.IsTrue(wavBytes.Length > 44, "WAV file must have at least 44-byte header.");

        Assert.AreEqual((byte)'R', wavBytes[0]);
        Assert.AreEqual((byte)'I', wavBytes[1]);
        Assert.AreEqual((byte)'F', wavBytes[2]);
        Assert.AreEqual((byte)'F', wavBytes[3]);

        Assert.AreEqual((byte)'W', wavBytes[8]);
        Assert.AreEqual((byte)'A', wavBytes[9]);
        Assert.AreEqual((byte)'V', wavBytes[10]);
        Assert.AreEqual((byte)'E', wavBytes[11]);

        Assert.AreEqual((byte)'f', wavBytes[12]);
        Assert.AreEqual((byte)'m', wavBytes[13]);
        Assert.AreEqual((byte)'t', wavBytes[14]);
        Assert.AreEqual((byte)' ', wavBytes[15]);

        compiler.Dispose();
    }

    // ---- Multimodal serialization ----

    [TestMethod]
    public async Task LlamaConnector_MultimodalSerialization_ContainsInputAudioAndImageUrl()
    {
        byte[] refAudio = new byte[100];
        byte[] synthAudio = new byte[200];
        byte[] spectrogram = new byte[50];

        var method = typeof(LlamaConnector).GetMethod(
            "BuildContentBlocks",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(method, "BuildContentBlocks must exist.");

        var contentBlocks = method.Invoke(null, new object?[]
        {
            "Improve this synth.", refAudio, synthAudio, spectrogram, true
        }) as List<object>;
        Assert.IsNotNull(contentBlocks);

        string json = JsonSerializer.Serialize(contentBlocks);
        Assert.IsTrue(json.Contains("input_audio"), "Audio must use the supported input_audio content type.");
        Assert.IsFalse(json.Contains("audio_url"), "audio_url is rejected by current llama-server builds.");
        Assert.IsTrue(json.Contains(Convert.ToBase64String(refAudio)), "Reference audio must be raw base64.");
        Assert.IsTrue(json.Contains(Convert.ToBase64String(synthAudio)), "Synth audio must be raw base64.");
        Assert.IsTrue(json.Contains("image_url"));

        await Task.CompletedTask;
    }

    // ---- Orchestrator happy path ----

    [TestMethod]
    public async Task Orchestrator_HappyPath_Completes()
    {
        var mockLlm = new MockLlamaConnector(code => @"```csharp
using System;
using ModularAudience.Audio.Synthesis.Runtime;
public class CustomSynth
{
    public static float[] Render(SynthRenderContext ctx)
    {
        var data = new float[ctx.FrameCount * ctx.Channels];
        for (int i = 0; i < data.Length; i++) data[i] = 0.3f;
        return data;
    }
}```");

        var compiler = new SynthCompiler();
        var descriptor = new CustomInstrumentDescriptor("HappySynth", "") { SampleRate = 22050, Channels = 1, MidiNote = 60, Bpm = 120, NoteBeats = 0.25 };

        var orch = new SynthRefinementOrchestrator(mockLlm, compiler, store, descriptor)
        {
            MaxIterations = 2,
            IncludeSpectrogram = false
        };

        string faultMessage = "";
        var tcs = new TaskCompletionSource<bool>();
        orch.ProgressChanged += p =>
        {
            if (p.State == SynthRefinementState.Faulted) faultMessage = p.Message ?? "";
            if (p.State == SynthRefinementState.Completed || p.State == SynthRefinementState.Faulted)
            {
                tcs.TrySetResult(p.State == SynthRefinementState.Completed);
            }
        };

        orch.Start();
        bool completed = await Task.WhenAny(tcs.Task, Task.Delay(30000)) == tcs.Task;
        if (completed) completed = await tcs.Task;
        else completed = orch.State == SynthRefinementState.Completed;
        Assert.IsTrue(completed, $"Orchestrator should complete successfully. State={orch.State}, History={orch.History.Count}, Fault={faultMessage}");

        orch.Dispose();
        compiler.Dispose();
    }

    [TestMethod]
    public async Task Orchestrator_WaitsForFeedback_AndSendsPrehearAudioOnNextIteration()
    {
        const string code = @"```csharp
using System;
using ModularAudience.Audio.Synthesis.Runtime;
public class CustomSynth
{
    public static float[] Render(SynthRenderContext ctx)
    {
        var data = new float[ctx.FrameCount * ctx.Channels];
        for (int i = 0; i < data.Length; i++) data[i] = 0.25f;
        return data;
    }
}```";
        var mockLlm = new MockLlamaConnector(_ => code);
        var compiler = new SynthCompiler();
        var descriptor = new CustomInstrumentDescriptor("FeedbackSynth", "")
        {
            SampleRate = 22050, Channels = 1, MidiNote = 60, Bpm = 120, NoteBeats = 1
        };

        var orch = new SynthRefinementOrchestrator(mockLlm, compiler, store, descriptor)
        {
            MaxIterations = 2,
            IncludeSpectrogram = false,
            PlayPrehearOnHotSwap = false,
            // Gate only after the first iteration so the test can finish on iteration 2.
            ShouldAwaitUserFeedback = () => mockLlm.Prompts.Count < 2
        };

        int feedbackSubmissions = 0;
        var tcs = new TaskCompletionSource<bool>();
        orch.ProgressChanged += p =>
        {
            if (p.State == SynthRefinementState.AwaitingFeedback)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(25);
                    orch.SubmitUserFeedback("make it brighter");
                    Interlocked.Increment(ref feedbackSubmissions);
                });
            }
            if (p.State is SynthRefinementState.Completed or SynthRefinementState.Faulted)
            {
                tcs.TrySetResult(p.State == SynthRefinementState.Completed);
            }
        };

        orch.Start();
        bool completed = await Task.WhenAny(tcs.Task, Task.Delay(30000)) == tcs.Task;
        if (completed) completed = await tcs.Task;
        Assert.IsTrue(completed, $"Orchestrator should complete after feedback. State={orch.State}");

        Assert.AreEqual(1, feedbackSubmissions, "Exactly one feedback submission should release the gate.");
        Assert.AreEqual(2, mockLlm.Prompts.Count, "Two LLM calls should have been made.");
        Assert.IsTrue(mockLlm.Prompts[1].Contains("make it brighter"), "User feedback must be in the next prompt.");
        Assert.IsTrue(mockLlm.Prompts[1].Contains("current 4-beat C4 pre-hear"), "Prompt must identify the carried audio.");
        Assert.AreEqual(2, mockLlm.SynthesizedAudio.Count);
        Assert.IsTrue(mockLlm.SynthesizedAudio[1].Length > mockLlm.SynthesizedAudio[0].Length,
            "The second LLM call must receive the 4-beat pre-hear rather than a short initial render.");

        orch.Dispose();
        compiler.Dispose();
    }

    // ---- Self-healing compile-error path ----

    [TestMethod]
    public async Task Orchestrator_SelfHealing_RetriesOnCompileError()
    {
        int callCount = 0;
        var mockLlm = new MockLlamaConnector(code =>
        {
            callCount++;
            if (callCount == 1)
            {
                return "```csharp\nthis is invalid C#;\n```";
            }
            return @"```csharp
using System;
using ModularAudience.Audio.Synthesis.Runtime;
public class CustomSynth
{
    public static float[] Render(SynthRenderContext ctx)
    {
        var data = new float[ctx.FrameCount * ctx.Channels];
        for (int i = 0; i < data.Length; i++) data[i] = 0.2f;
        return data;
    }
}```";
        });

        var compiler = new SynthCompiler();
        var descriptor = new CustomInstrumentDescriptor("HealSynth", "") { SampleRate = 22050, Channels = 1, MidiNote = 60, Bpm = 120, NoteBeats = 0.25 };

        var orch = new SynthRefinementOrchestrator(mockLlm, compiler, store, descriptor)
        {
            MaxIterations = 1,
            IncludeSpectrogram = false
        };

        string faultMessage = "";
        var tcs = new TaskCompletionSource<bool>();
        orch.ProgressChanged += p =>
        {
            if (p.State == SynthRefinementState.Faulted) faultMessage = p.Message ?? "";
            if (p.State == SynthRefinementState.Completed || p.State == SynthRefinementState.Faulted)
            {
                tcs.TrySetResult(p.State == SynthRefinementState.Completed);
            }
        };

        orch.Start();
        bool completed = await Task.WhenAny(tcs.Task, Task.Delay(30000)) == tcs.Task;
        if (completed) completed = await tcs.Task;
        else completed = orch.State == SynthRefinementState.Completed;
        Assert.IsTrue(completed, $"Orchestrator should self-heal and complete. State={orch.State}, History={orch.History.Count}, Fault={faultMessage}");
        Assert.IsTrue(callCount >= 2, "Should have made at least 2 LLM calls (initial + retry).");

        orch.Dispose();
        compiler.Dispose();
    }

    // ---- Cancellation ----

    [TestMethod]
    public async Task Orchestrator_Cancellation_SetsCancelledState()
    {
        int mockCallCount = 0;
        var mockLlm = new MockLlamaConnector(code => { mockCallCount++; Thread.Sleep(50); return "```csharp\n// delayed\n```"; });

        var compiler = new SynthCompiler();
        var descriptor = new CustomInstrumentDescriptor("CancelSynth", "") { SampleRate = 22050, Channels = 1, MidiNote = 60, Bpm = 120, NoteBeats = 0.25 };

        var orch = new SynthRefinementOrchestrator(mockLlm, compiler, store, descriptor)
        {
            MaxIterations = 5,
            IncludeSpectrogram = false
        };

        var tcs = new TaskCompletionSource<bool>();
        orch.ProgressChanged += p =>
        {
            if (p.State == SynthRefinementState.Cancelled || p.State == SynthRefinementState.Faulted || p.State == SynthRefinementState.Completed)
            {
                tcs.TrySetResult(p.State == SynthRefinementState.Cancelled);
            }
        };

        orch.Start();
        await Task.Delay(10);
        orch.Cancel();

        bool cancelled = await Task.WhenAny(tcs.Task, Task.Delay(10000)) == tcs.Task;
        if (cancelled) cancelled = await tcs.Task;
        else cancelled = orch.State == SynthRefinementState.Cancelled;
        Assert.IsTrue(cancelled, $"Orchestrator should be cancelled. State={orch.State}, MockCalls={mockCallCount}");

        orch.Dispose();
        compiler.Dispose();
    }

    // ---- Rollback (revert) ----

    [TestMethod]
    public void CustomInstrumentStore_SaveAndLoad_RoundTrips()
    {
        var descriptor = new CustomInstrumentDescriptor("RoundTrip", "public class X {}") { SampleRate = 44100, Channels = 2, MidiNote = 60 };
        store.Save(descriptor);

        var loaded = store.Load("RoundTrip");
        Assert.AreEqual("RoundTrip", loaded.Name);
        Assert.AreEqual("public class X {}", loaded.Code);
        Assert.AreEqual(44100, loaded.SampleRate);
        Assert.AreEqual(2, loaded.Channels);

        store.Delete("RoundTrip");
        Assert.IsFalse(store.Names().Contains("RoundTrip"));
    }

    // ---- Failed compile must keep the generated code visible ----

    [TestMethod]
    public async Task Orchestrator_FailedCompile_StillPublishesLlmCode()
    {
        const string invalidCode = "this is invalid C#;";
        var mockLlm = new MockLlamaConnector(_ => $"```csharp\n{invalidCode}\n```");
        var compiler = new SynthCompiler();
        var descriptor = new CustomInstrumentDescriptor("FailedCompileSynth", "")
        {
            SampleRate = 22050, Channels = 1, MidiNote = 60, Bpm = 120, NoteBeats = 0.25
        };
        var orch = new SynthRefinementOrchestrator(mockLlm, compiler, store, descriptor)
        {
            MaxIterations = 1,
            IncludeSpectrogram = false,
            AutoRetryCompileErrors = false
        };

        string lastStreamedCode = "";
        var messages = new System.Collections.Concurrent.ConcurrentBag<string>();
        var tcs = new TaskCompletionSource<bool>();
        orch.CodeStreaming += code => lastStreamedCode = code;
        orch.ProgressChanged += p =>
        {
            if (p.Message != null) messages.Add(p.Message);
            if (p.State == SynthRefinementState.Faulted || p.State == SynthRefinementState.Completed)
            {
                tcs.TrySetResult(p.State == SynthRefinementState.Faulted);
            }
        };

        orch.Start();
        bool faulted = await Task.WhenAny(tcs.Task, Task.Delay(30000)) == tcs.Task;
        if (faulted) faulted = await tcs.Task;
        else faulted = orch.State == SynthRefinementState.Faulted;

        Assert.IsTrue(faulted, $"Orchestrator should fault on invalid code. State={orch.State}");
        Assert.IsTrue(lastStreamedCode.Contains(invalidCode), "The invalid LLM code must still be published to the editor.");
        Assert.AreEqual(lastStreamedCode, descriptor.Code, "The descriptor must keep the exact code shown in the editor.");
        Assert.IsTrue(messages.Any(m => m.Contains("Showing latest LLM code in editor even though compilation failed")),
            "A log message must explain that the code is shown despite the compile failure.");

        orch.Dispose();
        compiler.Dispose();
    }

    // ---- Streaming failure must not discard partial code ----

    [TestMethod]
    public async Task LlamaConnector_StreamingFailure_PreservesPartialCode()
    {
        const string partialCode = "using System;\npublic class CustomSynth {";
        string contentJson = JsonSerializer.Serialize(partialCode);
        string sseText = $"data: {{\"choices\":[{{\"delta\":{{\"content\":{contentJson}}}}}]}}\n\n";

        var connector = new PartialFailureLlamaConnector(sseText);
        var chunks = new System.Collections.Generic.List<string>();
        string? result = await connector.SendMultimodalSynthPromptStreamingAsync(
            [1, 2, 3], [4, 5, 6], null, "Generate a synth.", false, chunk => chunks.Add(chunk));

        Assert.IsNotNull(result, "Partially streamed code must be returned after the stream fails.");
        Assert.IsTrue(result.Contains("public class CustomSynth"), "Returned text must contain the partial code.");
        Assert.AreEqual(1, chunks.Count, "The partial chunk must still be forwarded to the UI.");
        Assert.AreEqual(partialCode, chunks[0]);
    }

    // ---- Mock LlamaConnector for orchestrator tests ----

    private sealed class MockLlamaConnector : LlamaConnector
    {
        private readonly Func<string, string> responder;

        public MockLlamaConnector(Func<string, string> responder) : base("http://localhost:9931")
        {
            this.responder = responder;
        }

        public List<string> Prompts { get; } = [];
        public List<byte[]> SynthesizedAudio { get; } = [];

        public override Task<string?> SendMultimodalSynthPromptStreamingAsync(
            byte[] referenceAudio, byte[] synthesizedAudio, byte[]? spectrogramPng,
            string prompt, bool includeSpectrogram = false,
            Action<string>? onChunk = null)
        {
            this.Prompts.Add(prompt);
            this.SynthesizedAudio.Add((byte[])synthesizedAudio.Clone());
            string response = this.responder(prompt);
            if (onChunk != null && response != null)
            {
                // Simulate streaming by sending the whole response as one chunk.
                onChunk(response);
            }
            return Task.FromResult<string?>(response);
        }
    }

    private sealed class PartialFailureLlamaConnector : LlamaConnector
    {
        private readonly string sseText;

        public PartialFailureLlamaConnector(string sseText) : base("http://localhost:9931")
        {
            this.sseText = sseText;
        }

        protected override Task<HttpResponseMessage> PostChatAsync(object payload)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new FailingSseContent(this.sseText)
            });
        }
    }

    private sealed class FailingSseContent : HttpContent
    {
        private readonly byte[] bytes;

        public FailingSseContent(string sseText)
        {
            this.bytes = Encoding.UTF8.GetBytes(sseText);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(this.bytes, 0, this.bytes.Length);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = this.bytes.Length;
            return true;
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            return Task.FromResult<Stream>(new FailingSseStream(this.bytes));
        }
    }

    private sealed class FailingSseStream : Stream
    {
        private readonly byte[] bytes;
        private int position;

        public FailingSseStream(byte[] bytes)
        {
            this.bytes = bytes;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => this.bytes.Length;
        public override long Position
        {
            get => this.position;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (this.position >= this.bytes.Length)
            {
                throw new IOException("Simulated timeout after the first SSE chunk.");
            }

            int length = Math.Min(count, this.bytes.Length - this.position);
            Array.Copy(this.bytes, this.position, buffer, offset, length);
            this.position += length;
            return length;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return Task.FromResult(this.Read(buffer, offset, count));
            }
            catch (Exception ex)
            {
                return Task.FromException<int>(ex);
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
