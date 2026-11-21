using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Audio.Processing;

public sealed class AnalogAudioStemSeparator : IAudioStemSeparator
{
    private readonly IReadOnlyList<AnalogSeparationBand> bands;
    private readonly AnalogSeparationSettings settings;

    public AnalogAudioStemSeparator(
        IReadOnlyList<AnalogSeparationBand> bands,
        AnalogSeparationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(bands);
        ArgumentNullException.ThrowIfNull(settings);
        if (bands.Count == 0)
        {
            throw new ArgumentException("At least one analog separation band is required.", nameof(bands));
        }

        this.bands = bands.ToArray();
        this.settings = settings;
    }

    public string DisplayName => "Analog Separation";
    public int EstimatedStemCount => this.bands.Count;

    public async Task<AudioStemSeparationResult> SeparateAsync(
        AudioObj source,
        IProgress<AudioStemSeparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var mapped = progress == null
            ? null
            : new Progress<double>(fraction =>
                progress.Report(new AudioStemSeparationProgress(
                    fraction,
                    $"Separating analog band {Math.Min(this.bands.Count, (int)Math.Ceiling(fraction * this.bands.Count))}/{this.bands.Count}")));

        AnalogSeparationResult result = await AnalogSeparationProcessor
            .SeparateAsync(source, this.bands, this.settings, mapped, cancellationToken)
            .ConfigureAwait(false);

        AudioDecompileStem[] stems = result.Stems
            .Select((audio, index) => new AudioDecompileStem(
                string.IsNullOrWhiteSpace(audio.Name)
                    ? this.bands[Math.Min(index, this.bands.Count - 1)].Name
                    : audio.Name,
                audio))
            .ToArray();
        return new AudioStemSeparationResult(stems, []);
    }
}

public sealed class DeterministicAudioStemSeparator : IAudioStemSeparator
{
    private readonly DeterministicSeparationSettings settings;

    public DeterministicAudioStemSeparator(DeterministicSeparationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        this.settings = settings;
    }

    public string DisplayName => "Best-practice deterministic";
    public int EstimatedStemCount => this.settings.MaxComponents + 1;

    public async Task<AudioStemSeparationResult> SeparateAsync(
        AudioObj source,
        IProgress<AudioStemSeparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        IProgress<DeterministicSeparationProgress>? analysisProgress = progress == null
            ? null
            : new Progress<DeterministicSeparationProgress>(update =>
                progress.Report(new AudioStemSeparationProgress(update.Fraction * 0.5, update.Stage)));

        DeterministicSeparationAnalysis analysis = await DeterministicSeparationProcessor
            .AnalyzeAsync(source, this.settings, analysisProgress, cancellationToken)
            .ConfigureAwait(false);
        int[] sourceIds = analysis.Sources.Select(sourceDescriptor => sourceDescriptor.Id).ToArray();
        IProgress<DeterministicSeparationProgress>? separationProgress = progress == null
            ? null
            : new Progress<DeterministicSeparationProgress>(update =>
                progress.Report(new AudioStemSeparationProgress(0.5 + update.Fraction * 0.5, update.Stage)));

        DeterministicSeparationResult result = await DeterministicSeparationProcessor
            .SeparateAsync(analysis, sourceIds, separationProgress, cancellationToken)
            .ConfigureAwait(false);
        AudioDecompileStem[] stems = result.Stems
            .Select((audio, index) => new AudioDecompileStem(
                string.IsNullOrWhiteSpace(audio.Name) ? $"Source {index + 1}" : audio.Name,
                audio))
            .ToArray();

        List<string> warnings = [.. analysis.Warnings];
        if (!double.IsFinite(result.ReconstructionError) || result.ReconstructionError > 0.001)
        {
            warnings.Add($"Maximum mixture reconstruction error: {result.ReconstructionError:G6}.");
        }

        return new AudioStemSeparationResult(stems, warnings);
    }
}
