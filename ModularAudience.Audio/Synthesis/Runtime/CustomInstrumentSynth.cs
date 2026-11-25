using System.Reflection;
using System.Threading;

namespace ModularAudience.Audio.Synthesis.Runtime;

/// <summary>
/// Wraps an LLM-generated type (compiled via Roslyn) as an <see cref="IRuntimeProceduralSynth"/>.
/// The generated type must expose a public static method matching:
///   public static float[] Render(int sampleRate, int channels, int frameCount, double noteFrequencyHz)
/// or an instance method with the same signature on a type with a parameterless constructor.
/// </summary>
public sealed class CustomInstrumentSynth : IRuntimeProceduralSynth
{
    private readonly CustomInstrumentDescriptor descriptor;
    private readonly Type synthType;
    private readonly MethodInfo? renderMethod;
    private readonly object? instance;
    private readonly bool isStatic;
    private float[] renderBuffer;

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
                    [typeof(int), typeof(int), typeof(int), typeof(double)],
                    null) is not null)
            ?? candidateTypes.FirstOrDefault(t =>
                t.GetMethod("Render",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance,
                    null,
                    [typeof(int), typeof(int), typeof(int)],
                    null) is not null)
            ?? throw new InvalidOperationException(
                $"Compiled assembly does not contain a public 'Render(int, int, int, double)' or 'Render(int, int, int)' method.");

        var render = this.synthType.GetMethod("Render",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance,
            null, [typeof(int), typeof(int), typeof(int), typeof(double)], null);

        if (render is not null && render.IsStatic)
        {
            this.renderMethod = render;
            this.isStatic = true;
            this.instance = null;
        }
        else
        {
            render = this.synthType.GetMethod("Render",
                BindingFlags.Public | BindingFlags.Instance,
                null, [typeof(int), typeof(int), typeof(int), typeof(double)], null);

            if (render is not null)
            {
                this.renderMethod = render;
                this.isStatic = false;
                var ctor = this.synthType.GetConstructor(Type.EmptyTypes);
                this.instance = ctor?.Invoke(null)
                    ?? throw new InvalidOperationException($"Type '{this.synthType.FullName}' has no parameterless constructor.");
            }
            else
            {
                // Fallback: Render(int, int, int)
                render = this.synthType.GetMethod("Render",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance,
                    null, [typeof(int), typeof(int), typeof(int)], null);
                if (render is not null && render.IsStatic)
                {
                    this.renderMethod = render;
                    this.isStatic = true;
                    this.instance = null;
                }
                else
                {
                    render = this.synthType.GetMethod("Render",
                        BindingFlags.Public | BindingFlags.Instance,
                        null, [typeof(int), typeof(int), typeof(int)], null);
                    this.renderMethod = render;
                    this.isStatic = false;
                    this.instance = this.synthType.GetConstructor(Type.EmptyTypes)?.Invoke(null)
                        ?? throw new InvalidOperationException($"Type '{this.synthType.FullName}' has no parameterless constructor.");
                }
            }
        }

        // Pre-allocate a render buffer for the maximum reasonable frame count.
        this.renderBuffer = new float[this.SampleRate * this.Channels * 4]; // 4 seconds max block
    }

    /// <summary>
    /// Renders <paramref name="frameCount"/> frames by invoking the compiled Render method.
    /// </summary>
    public int Render(Span<float> buffer, int frameCount, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        int totalSamples = frameCount * this.Channels;
        if (totalSamples > this.renderBuffer.Length)
        {
            Array.Resize(ref this.renderBuffer, totalSamples);
        }

        double noteFrequencyHz = 440.0 * Math.Pow(2.0, (this.descriptor.MidiNote - 69) / 12.0);

        object?[] args4 = [this.SampleRate, this.Channels, frameCount, noteFrequencyHz];
        object?[] args3 = [this.SampleRate, this.Channels, frameCount];

        float[] result;
        try
        {
            if (this.isStatic)
            {
                if (args4.Length == 4 && this.renderMethod!.GetParameters().Length == 4)
                {
                    result = (float[])this.renderMethod.Invoke(null, args4)!;
                }
                else
                {
                    result = (float[])this.renderMethod.Invoke(null, args3)!;
                }
            }
            else
            {
                if (this.renderMethod!.GetParameters().Length == 4)
                {
                    result = (float[])this.renderMethod.Invoke(this.instance, args4)!;
                }
                else
                {
                    result = (float[])this.renderMethod.Invoke(this.instance, args3)!;
                }
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

        int copyCount = Math.Min(totalSamples, result.Length);
        for (int i = 0; i < copyCount; i++)
        {
            buffer[i] = result[i];
        }

        // Zero-fill remaining samples if the synth produced fewer than requested.
        for (int i = copyCount; i < totalSamples; i++)
        {
            buffer[i] = 0f;
        }

        return totalSamples;
    }
}
