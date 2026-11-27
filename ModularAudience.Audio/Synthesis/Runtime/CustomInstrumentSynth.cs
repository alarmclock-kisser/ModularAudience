using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace ModularAudience.Audio.Synthesis.Runtime;

/// <summary>
/// Wraps an LLM-generated type (compiled via Roslyn) as an <see cref="IRuntimeProceduralSynth"/>.
/// The generated type must expose a public method matching
/// <c>public static float[] Render(SynthRenderContext ctx)</c>, or an instance method with
/// the same signature on a type with a parameterless constructor.
/// </summary>
public sealed class CustomInstrumentSynth : IRuntimeProceduralSynth
{
    private readonly CustomInstrumentDescriptor descriptor;
    private readonly Type synthType;
    private readonly MethodInfo? renderMethod;
    private readonly object? instance;
    private readonly bool isStatic;

    public string Name => this.descriptor.Name;
    public int SampleRate => this.descriptor.SampleRate;
    public int Channels => this.descriptor.Channels;

    public CustomInstrumentSynth(CustomInstrumentDescriptor descriptor, Assembly assembly)
    {
        this.descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));

        // Look for a type named CustomSynth, InstrumentSynth, or any public type with a Render method.
        var candidateTypes = assembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && !t.IsInterface)
            .ToArray();

        this.synthType = candidateTypes.FirstOrDefault(t =>
                t.GetMethod("Render",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance,
                    null,
                    [typeof(SynthRenderContext)],
                    null) is not null)
            ?? throw new InvalidOperationException(
                "Compiled assembly does not contain a public 'Render(SynthRenderContext)' method." +
                Environment.NewLine + SynthContract.LogDescription +
                Environment.NewLine + "Public types found: " +
                (candidateTypes.Length == 0 ? "<none>" : string.Join(", ", candidateTypes.Select(t => t.FullName ?? t.Name))) +
                Environment.NewLine + "Public Render methods found: " + DescribeRenderMethods(candidateTypes));

        var render = this.synthType.GetMethod("Render",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance,
            null, [typeof(SynthRenderContext)], null);

        if (render is not null && render.IsStatic)
        {
            this.renderMethod = render;
            this.isStatic = true;
            this.instance = null;
        }
        else
        {
            this.renderMethod = render;
            this.isStatic = false;
            var ctor = this.synthType.GetConstructor(Type.EmptyTypes);
            this.instance = ctor?.Invoke(null)
                ?? throw new InvalidOperationException($"Type '{this.synthType.FullName}' has no parameterless constructor.");
        }
    }

    /// <summary>
    /// Renders <paramref name="frameCount"/> frames by invoking the compiled Render method.
    /// </summary>
    public int Render(Span<float> buffer, int frameCount, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        // LLM-generated DSP code is written for stereo (it writes to i * Channels + 1).
        // When the output is mono, render in stereo and downmix to avoid an
        // IndexOutOfRangeException on the generated buffer.
        bool downmix = this.Channels == 1;
        int renderChannels = downmix ? 2 : this.Channels;
        int totalSamples = frameCount * this.Channels;

        // Match the built-in synth: the key note and the pitch-offset knob both shift
        // the base frequency. Generated DSP code can additionally use ctx.PitchSemitones.
        double noteFrequencyHz = 440.0 * Math.Pow(2.0, (this.descriptor.MidiNote - 69 + this.descriptor.PitchSemitones) / 12.0);

        var ctx = new SynthRenderContext
        {
            SampleRate = this.SampleRate,
            Channels = renderChannels,
            FrameCount = frameCount,
            NoteFrequencyHz = noteFrequencyHz,
            Bpm = this.descriptor.Bpm,
            NoteBeats = this.descriptor.NoteBeats,
            // DurationMultiplier is exposed separately to generated code; the sample
            // synthesizer already applies it to the output buffer length, so do not
            // multiply it into GateSeconds a second time.
            GateSeconds = 60.0 / Math.Max(1.0, this.descriptor.Bpm) * this.descriptor.NoteBeats,
            MidiNote = this.descriptor.MidiNote,
            GainPercent = this.descriptor.GainPercent,
            PitchSemitones = this.descriptor.PitchSemitones,
            DurationMultiplier = this.descriptor.DurationMultiplier,
            AttackMs = this.descriptor.AttackMs,
            DecaySeconds = this.descriptor.DecaySeconds,
            SustainPercent = this.descriptor.SustainPercent,
            ReleaseSeconds = this.descriptor.ReleaseSeconds,
            Brightness = this.descriptor.Brightness,
            Body = this.descriptor.Body,
            Noise = this.descriptor.Noise,
            Drive = this.descriptor.Drive,
            DetuneCents = this.descriptor.DetuneCents,
            VibratoCents = this.descriptor.VibratoCents,
            VibratoHz = this.descriptor.VibratoHz,
            CutoffHz = this.descriptor.CutoffHz,
            Resonance = this.descriptor.Resonance,
            FilterEnvelope = this.descriptor.FilterEnvelope,
            RoomPercent = this.descriptor.RoomPercent,
            StereoWidth = this.descriptor.StereoWidth,
            Seed = this.descriptor.Seed
        };

        object?[] args = [ctx];

        float[] result;
        try
        {
            if (this.isStatic)
            {
                result = (float[])this.renderMethod!.Invoke(null, args)!;
            }
            else
            {
                result = (float[])this.renderMethod!.Invoke(this.instance, args)!;
            }
        }
        catch (TargetInvocationException ex)
        {
            throw new InvalidOperationException($"Synth Render threw: {ex.InnerException?.Message ?? ex.Message}", ex.InnerException);
        }

        if (result == null)
        {
            return 0;
        }

        int framesProduced = Math.Min(frameCount, result.Length / renderChannels);
        for (int i = 0; i < framesProduced; i++)
        {
            if (downmix)
            {
                buffer[i] = (result[i * 2] + result[i * 2 + 1]) * 0.5f;
            }
            else
            {
                for (int c = 0; c < this.Channels; c++)
                {
                    buffer[i * this.Channels + c] = result[i * renderChannels + c];
                }
            }
        }

        // Zero-fill remaining samples if the synth produced fewer than requested.
        int filled = framesProduced * this.Channels;
        for (int i = filled; i < totalSamples; i++)
        {
            buffer[i] = 0f;
        }

        return totalSamples;
    }

    private static string DescribeRenderMethods(IEnumerable<Type> types)
    {
        var methods = types
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                .Where(m => m.Name == "Render"))
            .Select(m =>
            {
                string modifier = m.IsStatic ? "static " : "instance ";
                string parameters = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName ?? p.ParameterType.Name));
                return $"{modifier}{m.ReturnType.Name} {m.DeclaringType?.FullName}.Render({parameters})";
            })
            .ToArray();

        return methods.Length == 0 ? "<none>" : string.Join(" | ", methods);
    }
}

/// <summary>
/// Compact, authoritative description of the runtime synth contract used in errors and prompts.
/// </summary>
public static class SynthContract
{
    public const string RequiredSystemUsing = "using System;";
    public const string RequiredUsing = "using ModularAudience.Audio.Synthesis.Runtime;";
    public const string ExpectedSignature = "public static float[] Render(SynthRenderContext ctx)";
    public static string RequiredUsings => RequiredSystemUsing + Environment.NewLine + RequiredUsing;

    // Compact record-style shape of the DTO. The host already supplies this type; generated
    // code must use it via the using directive and must not declare it again.
    public const string DtoShape =
        "host-supplied readonly SynthRenderContext with properties: int SampleRate, int Channels, int FrameCount, " +
        "double NoteFrequencyHz, double Bpm, double NoteBeats, double GateSeconds, int MidiNote, double GainPercent, " +
        "double PitchSemitones, double DurationMultiplier, double AttackMs, double DecaySeconds, double SustainPercent, " +
        "double ReleaseSeconds, double Brightness, double Body, double Noise, double Drive, double DetuneCents, " +
        "double VibratoCents, double VibratoHz, double CutoffHz, double Resonance, double FilterEnvelope, " +
        "double RoomPercent, double StereoWidth, int Seed";

    public static string LogDescription => string.Join(Environment.NewLine,
        "Expected custom instrument contract:",
        RequiredUsings,
        ExpectedSignature + ";",
        "DTO shape (for reference only - do NOT redeclare it): " + DtoShape,
        "Return float[] with ctx.FrameCount * ctx.Channels interleaved samples in [-1, 1].");
}
