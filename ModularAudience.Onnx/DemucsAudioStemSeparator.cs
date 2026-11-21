using ModularAudience.Audio;
using ModularAudience.Audio.Processing;

namespace ModularAudience.Onnx;

public sealed class DemucsAudioStemSeparator : IAudioStemSeparator
{
    private readonly string modelPath;
    private readonly IReadOnlyList<string> modelDirectories;
    private readonly IReadOnlyList<DemucsLoadOptions> options;
    private readonly TimeSpan keepModelLoaded;

    public DemucsAudioStemSeparator(
        string modelPath,
        IReadOnlyList<DemucsLoadOptions> options,
        IEnumerable<string> modelDirectories,
        TimeSpan keepModelLoaded)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            throw new ArgumentException("Select an existing Demucs ONNX model.", nameof(modelPath));
        }

        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(modelDirectories);
        if (options.Count == 0)
        {
            throw new ArgumentException("At least one Demucs execution configuration is required.", nameof(options));
        }

        this.modelPath = Path.GetFullPath(modelPath);
        this.options = options.Select(CloneOptions).ToArray();
        this.modelDirectories = modelDirectories.ToArray();
        this.keepModelLoaded = keepModelLoaded < TimeSpan.Zero ? TimeSpan.Zero : keepModelLoaded;
    }

    public string DisplayName => "Demucs ONNX";
    public int EstimatedStemCount => 6;

    public async Task<AudioStemSeparationResult> SeparateAsync(
        AudioObj source,
        IProgress<AudioStemSeparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        List<SharedModelPool<OnnxService>.Lease> leases = [];
        AudioDecompileStem?[] stems = [];
        double[] fractions = [];
        try
        {
            progress?.Report(new AudioStemSeparationProgress(0, "Loading ONNX session"));
            Task<SharedModelPool<OnnxService>.Lease?>[] loadTasks = this.options
                .Select(option => DemucsModelCache.AcquireAsync(
                    this.modelPath, option, this.modelDirectories, allowLoad: true, cancellationToken))
                .ToArray();
            SharedModelPool<OnnxService>.Lease?[] loaded;
            try
            {
                loaded = await Task.WhenAll(loadTasks).ConfigureAwait(false);
            }
            catch
            {
                foreach (Task<SharedModelPool<OnnxService>.Lease?> task in loadTasks)
                {
                    if (task.Status == TaskStatus.RanToCompletion && task.Result is { } lease)
                    {
                        lease.KeepAlive = TimeSpan.Zero;
                        lease.Dispose();
                    }
                }

                throw;
            }

            try
            {
                if (loaded.Any(lease => lease is null))
                {
                    throw new InvalidOperationException(
                        "The selected Demucs model could not be loaded.");
                }

                leases = loaded.Select(lease => lease!).ToList();
            }
            catch
            {
                foreach (SharedModelPool<OnnxService>.Lease? lease in loaded)
                {
                    if (lease is null)
                    {
                        continue;
                    }

                    lease.KeepAlive = TimeSpan.Zero;
                    lease.Dispose();
                }

                throw;
            }

            string[] stemNames = leases[0].Model.AvailableStems
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (stemNames.Length == 0)
            {
                throw new NotSupportedException("The selected Demucs model does not expose any stems.");
            }

            stems = new AudioDecompileStem?[stemNames.Length];
            fractions = new double[stemNames.Length];
            int[][] assignments = AssignStemsToDevices(stemNames.Length, leases.Count);
            var progressGate = new object();
            using var workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task[] workers = leases.Select((lease, deviceIndex) => Task.Run(async () =>
            {
                await lease.Operation.WaitAsync(workerCancellation.Token).ConfigureAwait(false);
                try
                {
                    try
                    {
                        foreach (int stemIndex in assignments[deviceIndex])
                        {
                            workerCancellation.Token.ThrowIfCancellationRequested();
                            string stemName = stemNames[stemIndex];
                            IProgress<double> stemProgress = new Progress<double>(fraction =>
                            {
                                lock (progressGate)
                                {
                                    fractions[stemIndex] = Math.Clamp(fraction, 0, 1);
                                    progress?.Report(new AudioStemSeparationProgress(
                                        fractions.Average(),
                                        $"Stem {stemIndex + 1}/{stemNames.Length}: {stemName}"));
                                }
                            });
                            Dictionary<string, float[]> extracted = await lease.Model
                                .ExtractStemsAsync(source, [stemName], stemProgress, workerCancellation.Token)
                                .ConfigureAwait(false);
                            AudioObj audio = AudioDecompileAudio.CreateStem(
                                source,
                                extracted[stemName],
                                $"{GetSourceName(source)}_{stemName}");
                            try
                            {
                                stems[stemIndex] = new AudioDecompileStem(stemName, audio);
                            }
                            catch
                            {
                                audio.Dispose();
                                throw;
                            }
                        }
                    }
                    catch
                    {
                        workerCancellation.Cancel();
                        throw;
                    }
                }
                finally
                {
                    lease.Operation.Release();
                }
            }, workerCancellation.Token)).ToArray();

            try
            {
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            catch
            {
                workerCancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new AudioStemSeparationProgress(
                1,
                $"{stemNames.Length} Demucs stems separated"));
            AudioDecompileStem[] completed = stems
                .Select(stem => stem ?? throw new InvalidOperationException("A Demucs stem was not produced."))
                .ToArray();
            return new AudioStemSeparationResult(completed, GetWarnings(leases));
        }
        catch
        {
            foreach (AudioDecompileStem? stem in stems)
            {
                stem?.Audio.Dispose();
            }

            throw;
        }
        finally
        {
            foreach (SharedModelPool<OnnxService>.Lease lease in leases)
            {
                lease.KeepAlive = this.keepModelLoaded;
                lease.Dispose();
            }
        }
    }

    private static DemucsLoadOptions CloneOptions(DemucsLoadOptions options)
    {
        return new DemucsLoadOptions
        {
            UseDirectML = options.UseDirectML,
            DeviceId = options.DeviceId,
            AllowCpuFallback = options.AllowCpuFallback,
            AllowCpuOperators = options.AllowCpuOperators,
            DisableDirectMLGraphFusion = options.DisableDirectMLGraphFusion,
            FourierWorkers = options.FourierWorkers,
            ChunkOverlap = options.ChunkOverlap,
            CudaDeviceId = options.CudaDeviceId,
            UseCudaCuFft = options.UseCudaCuFft,
            CudaToolkitVersion = options.CudaToolkitVersion,
            DeviceLuid = options.DeviceLuid,
            GraphOptimization = options.GraphOptimization,
            ExecutionMode = options.ExecutionMode,
            EnableMemoryPattern = options.EnableMemoryPattern,
            EnableCpuMemoryArena = options.EnableCpuMemoryArena,
            IntraOpThreads = options.IntraOpThreads,
            InterOpThreads = options.InterOpThreads,
            EnableProfiling = options.EnableProfiling,
            ProfileOutputPathPrefix = options.ProfileOutputPathPrefix,
            LogSession = options.LogSession,
            DisableSpinning = options.DisableSpinning,
            DisablePrepacking = options.DisablePrepacking,
            OptimizedModelPath = options.OptimizedModelPath,
            SessionConfigEntries = new Dictionary<string, string>(options.SessionConfigEntries ?? [])
        };
    }

    private static string[] GetWarnings(IEnumerable<SharedModelPool<OnnxService>.Lease> leases)
    {
        return leases.Select(lease => lease.Model.LoadWarning)
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .ToArray();
    }

    private static int[][] AssignStemsToDevices(int stemCount, int deviceCount)
    {
        var assignments = Enumerable.Range(0, deviceCount).Select(_ => new List<int>()).ToArray();
        for (int index = 0; index < stemCount; index++)
        {
            assignments[index % deviceCount].Add(index);
        }

        return assignments.Select(items => items.ToArray()).ToArray();
    }

    private static string GetSourceName(AudioObj source) =>
        string.IsNullOrWhiteSpace(source.Name) ? "Audio" : source.Name.Trim();
}
