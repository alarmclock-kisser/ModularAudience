using System.Text.Json.Serialization;

namespace ModularAudience.Generators;

public record BeatEngineConfig
{
    public int SampleCount { get; init; } = 8;
    public int Bars { get; init; } = 1;
    public int BeatsPerBar { get; init; } = 4;
    public int StepsPerBeat { get; init; } = 4;
    public float LearningRate { get; init; } = .2f;
    public float Temperature { get; init; } = 1;
    public float WeightDecay { get; init; } = .01f;
    public float MinWeight { get; init; } = -10;
    public float MaxWeight { get; init; } = 10;
    public int ThreadCount { get; init; } = Environment.ProcessorCount;
    public int Interleaved { get; init; } = 1;
    [JsonIgnore] public int TotalSteps => checked(Bars * BeatsPerBar * StepsPerBeat);
    [JsonIgnore] public int MaximumHits => Math.Min(SampleCount, Interleaved + 1);

    public void Validate()
    {
        if (SampleCount is < 1 or > 64 || Bars is < 1 or > 64 || BeatsPerBar is < 1 or > 16 ||
            StepsPerBeat is < 1 or > 32 || TotalSteps > 4096 || (long)TotalSteps * SampleCount > 131072 ||
            Interleaved is < 0 or > 16 || ThreadCount < 1 || ThreadCount > Math.Max(128, Environment.ProcessorCount))
            throw new ArgumentException("Use 1–64 sample slots, at most 4096 steps and 131072 step/slot cells.");
        if (!float.IsFinite(LearningRate) || LearningRate is < 0 or > 1 || !float.IsFinite(Temperature) || Temperature is < .01f or > 10 ||
            !float.IsFinite(WeightDecay) || WeightDecay is < 0 or > 1 || !float.IsFinite(MinWeight) || !float.IsFinite(MaxWeight) ||
            MinWeight < -100 || MaxWeight > 100 || MinWeight >= MaxWeight || MinWeight > 0 || MaxWeight < 0)
            throw new ArgumentException("Invalid learning parameters. Weight bounds must include zero; temperature must be positive.");
    }
}

public sealed class BeatNode : IDisposable
{
    public string Id { get; }
    public float[,] Weights { get; }
    private readonly ReaderWriterLockSlim gate = new();
    public BeatNode(string id, int totalSteps, int sampleCount) { Id = id; Weights = new float[totalSteps, sampleCount]; }
    public void ReadLock(Action action) { gate.EnterReadLock(); try { action(); } finally { gate.ExitReadLock(); } }
    public T ReadLock<T>(Func<T> action) { gate.EnterReadLock(); try { return action(); } finally { gate.ExitReadLock(); } }
    public void WriteLock(Action action) { gate.EnterWriteLock(); try { action(); } finally { gate.ExitWriteLock(); } }
    public void Dispose() => gate.Dispose();
}

public sealed record BeatPatternRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public bool[][] Pattern { get; init; } = [];
    public float? Feedback { get; set; }
    public int Revision { get; init; }
}

public sealed record BeatBranchSnapshot
{
    public string Id { get; init; } = "root";
    public string Name { get; set; } = "Root";
    public string? ParentId { get; init; }
    public string? SourcePatternId { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public float Mutation { get; init; }
    public int Revision { get; set; }
    public int FeedbackCount { get; set; }
    public int ManualEditCount { get; set; }
    public float[][] Weights { get; set; } = [];
    public float[][] DensityWeights { get; set; } = [];
    public List<BeatPatternRecord> Patterns { get; init; } = [];
}

public sealed record BeatEngineSnapshot
{
    public int Version { get; init; } = 1;
    public BeatEngineConfig Config { get; init; } = new();
    public List<BeatBranchSnapshot> Branches { get; init; } = [];
    public long TrainingIterations { get; init; }
}

/// <summary>
/// Feedback-trained step/slot and step/hit-count categorical model. Branches inherit independent
/// weight copies. This is a probabilistic rhythm model, not an audio neural network or temporal predictor.
/// Audio never enters the model: slot order and the timing grid define compatibility.
/// </summary>
public sealed class AsyncBeatGraphEngine : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, BeatBranchSnapshot> branches = new(StringComparer.Ordinal);
    private bool disposed;
    private long trainingIterations;
    public BeatEngineConfig Config { get; private set; }
    public void SetThreadCount(int threadCount)
    {
        if (threadCount < 1 || threadCount > Math.Max(128, Environment.ProcessorCount)) throw new ArgumentOutOfRangeException(nameof(threadCount));
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Config = Config with { ThreadCount = threadCount };
        }
    }
    public void UpdateLearningSettings(float learningRate, float temperature, float decay, float minWeight, float maxWeight)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var updated = Config with { LearningRate = learningRate, Temperature = temperature, WeightDecay = decay,
                MinWeight = minWeight, MaxWeight = maxWeight };
            updated.Validate();
            bool boundsChanged = updated.MinWeight != Config.MinWeight || updated.MaxWeight != Config.MaxWeight;
            if (boundsChanged)
                foreach (var branch in branches.Values)
                {
                    bool changed = false;
                    foreach (var matrix in new[] { branch.Weights, branch.DensityWeights })
                        foreach (var row in matrix)
                            for (int column = 0; column < row.Length; column++)
                            {
                                float clamped = Math.Clamp(row[column], minWeight, maxWeight);
                                changed |= clamped != row[column];
                                row[column] = clamped;
                            }
                    if (changed) { branch.Revision++; branch.ManualEditCount++; }
                }
            Config = updated;
        }
    }
    public (long Total, int Branch) GetTrainingIterations(string nodeId)
    {
        lock (gate) return (trainingIterations, Branch(nodeId).FeedbackCount);
    }

    public AsyncBeatGraphEngine(BeatEngineConfig config)
    {
        config.Validate();
        Config = config;
        branches.Add("root", NewBranch("root", "Root"));
    }

    private BeatBranchSnapshot NewBranch(string id, string name, string? parent = null, float mutation = 0, string? sourcePattern = null) => new()
    {
        Id = id, Name = name, ParentId = parent, Mutation = mutation, SourcePatternId = sourcePattern,
        Weights = Matrix(Config.TotalSteps, Config.SampleCount), DensityWeights = Matrix(Config.TotalSteps, Config.MaximumHits + 1)
    };

    private static float[][] Matrix(int rows, int columns) => Enumerable.Range(0, rows).Select(_ => new float[columns]).ToArray();
    private BeatBranchSnapshot Branch(string id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return branches.TryGetValue(id, out var branch) ? branch : throw new KeyNotFoundException("Training branch not found: " + id);
    }

    public BeatEngineSnapshot Snapshot()
    {
        lock (gate) return new() { Config = Config, TrainingIterations = trainingIterations, Branches = branches.Values.Select(CloneBranch).ToList() };
    }

    private static float[][] Copy(float[][] matrix) => matrix.Select(row => row.ToArray()).ToArray();
    public static bool[][] CopyPattern(bool[][] pattern) => pattern.Select(row => row.ToArray()).ToArray();
    private static BeatBranchSnapshot CloneBranch(BeatBranchSnapshot branch) => branch with
    {
        Weights = Copy(branch.Weights), DensityWeights = Copy(branch.DensityWeights),
        Patterns = branch.Patterns.Select(record => record with { Pattern = CopyPattern(record.Pattern) }).ToList()
    };

    public static AsyncBeatGraphEngine Restore(BeatEngineSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Version != 1) throw new InvalidDataException("Unsupported training model version.");
        snapshot.Config.Validate();
        if (snapshot.Branches is null || snapshot.Branches.Count is < 1 or > 2048 || snapshot.Branches.Count(branch => branch.Id == "root") != 1)
            throw new InvalidDataException("The training tree must contain exactly one root and at most 2048 branches.");
        var engine = new AsyncBeatGraphEngine(snapshot.Config);
        try
        {
            engine.branches.Clear();
            long cells = 0, patternCells = 0;
            var patternIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var branch in snapshot.Branches)
            {
                if (string.IsNullOrWhiteSpace(branch.Id) || branch.Id.Length > 120 || string.IsNullOrWhiteSpace(branch.Name) || branch.Name.Length > 200 ||
                    branch.Revision < 0 || branch.FeedbackCount < 0 || branch.ManualEditCount < 0 || !float.IsFinite(branch.Mutation) || branch.Mutation is < 0 or > 5 ||
                    !engine.branches.TryAdd(branch.Id, branch)) throw new InvalidDataException("Invalid or duplicate training branch.");
                ValidateMatrix(branch.Weights, snapshot.Config.SampleCount);
                ValidateMatrix(branch.DensityWeights, snapshot.Config.MaximumHits + 1);
                if (branch.Patterns is null) throw new InvalidDataException("Missing pattern history.");
                foreach (var record in branch.Patterns)
                {
                    engine.ValidatePattern(record.Pattern);
                    patternCells += (long)snapshot.Config.SampleCount * snapshot.Config.TotalSteps;
                    if (patternCells > 32_000_000 || record.Revision < 0 || record.Revision > branch.Revision ||
                        string.IsNullOrEmpty(record.Id) || !patternIds.Add(record.Id) || record.Feedback is { } rating && (!float.IsFinite(rating) || rating is < 0 or > 1))
                        throw new InvalidDataException("Invalid pattern history.");
                }
            }
            if (engine.branches["root"].ParentId is not null) throw new InvalidDataException("Root cannot have a parent.");
            foreach (var branch in engine.branches.Values)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var current = branch;
                while (current.ParentId is { } parent)
                {
                    if (!visited.Add(current.Id) || !engine.branches.TryGetValue(parent, out current!)) throw new InvalidDataException("Cyclic or disconnected training tree.");
                }
                if (current.Id != "root") throw new InvalidDataException("Disconnected training branch.");
            }
            foreach (string id in engine.branches.Keys.ToArray()) engine.branches[id] = CloneBranch(engine.branches[id]);
            long branchIterations = engine.branches.Values.Sum(branch => (long)branch.FeedbackCount);
            if (snapshot.TrainingIterations < branchIterations || snapshot.TrainingIterations < 0)
                throw new InvalidDataException("Invalid training iteration counter.");
            engine.trainingIterations = snapshot.TrainingIterations;
            return engine;

            void ValidateMatrix(float[][] matrix, int columns)
            {
                if (matrix is null || matrix.Length != snapshot.Config.TotalSteps || matrix.Any(row => row is null || row.Length != columns ||
                    row.Any(weight => !float.IsFinite(weight) || weight < snapshot.Config.MinWeight || weight > snapshot.Config.MaxWeight)))
                    throw new InvalidDataException("Invalid training weight matrix.");
                cells += (long)matrix.Length * columns;
                if (cells > 16_000_000) throw new InvalidDataException("Training model is too large.");
            }
        }
        catch { engine.Dispose(); throw; }
    }

    public Task<bool[][]> GeneratePatternAsync(string nodeId, float? temperatureOverride = null, CancellationToken ct = default) => Task.Run(() =>
    {
        lock (gate)
        {
            var branch = Branch(nodeId);
            float temperature = temperatureOverride ?? Config.Temperature;
            if (!float.IsFinite(temperature) || temperature <= 0) throw new ArgumentOutOfRangeException(nameof(temperatureOverride));
            var pattern = Enumerable.Range(0, Config.SampleCount).Select(_ => new bool[Config.TotalSteps]).ToArray();
            var candidates = new List<int>(Config.SampleCount);
            for (int step = 0; step < Config.TotalSteps; step++)
            {
                ct.ThrowIfCancellationRequested();
                int count = Choose(branch.DensityWeights[step], temperature, null);
                candidates.Clear();
                for (int slot = 0; slot < Config.SampleCount; slot++) candidates.Add(slot);
                for (int hit = 0; hit < count; hit++)
                {
                    int slot = Choose(branch.Weights[step], temperature, candidates);
                    pattern[slot][step] = true;
                    candidates.Remove(slot);
                }
            }
            branch.Patterns.Add(new() { Pattern = CopyPattern(pattern), Revision = branch.Revision });
            return pattern;
        }
    }, ct);

    private static int Choose(float[] weights, float temperature, List<int>? candidates)
    {
        var indices = candidates ?? Enumerable.Range(0, weights.Length).ToList();
        double maximum = indices.Max(index => (double)weights[index] / temperature);
        double sum = indices.Sum(index => Math.Exp(weights[index] / (double)temperature - maximum));
        double roll = Random.Shared.NextDouble() * sum;
        foreach (int index in indices)
        {
            roll -= Math.Exp(weights[index] / (double)temperature - maximum);
            if (roll <= 0) return index;
        }
        return indices[^1];
    }

    public async Task<int[]> GenerateSequenceAsync(string nodeId, float? temperatureOverride = null, CancellationToken ct = default)
    {
        var pattern = await GeneratePatternAsync(nodeId, temperatureOverride, ct);
        return Enumerable.Range(0, Config.TotalSteps).Select(step => Enumerable.Range(0, Config.SampleCount).FirstOrDefault(slot => pattern[slot][step], -1)).ToArray();
    }

    public async Task<List<int[]>> GenerateBatchAsync(string nodeId, int batchSize, CancellationToken ct = default)
    {
        if (batchSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var sequences = new int[batchSize][];
        await Parallel.ForEachAsync(Enumerable.Range(0, batchSize), new ParallelOptions { MaxDegreeOfParallelism = Config.ThreadCount, CancellationToken = ct },
            async (index, token) => sequences[index] = await GenerateSequenceAsync(nodeId, null, token));
        return sequences.ToList();
    }

    public Task ApplyFeedbackAsync(string nodeId, int[] sequence, float feedbackValue, CancellationToken ct = default)
    {
        if (sequence.Length != Config.TotalSteps || sequence.Any(slot => slot < -1 || slot >= Config.SampleCount)) throw new ArgumentException("Invalid sequence.");
        var pattern = Enumerable.Range(0, Config.SampleCount).Select(slot => sequence.Select(value => value == slot).ToArray()).ToArray();
        return ApplyFeedbackAsync(nodeId, pattern, feedbackValue, ct);
    }

    public Task ApplyFeedbackAsync(string nodeId, bool[][] pattern, float feedbackValue, CancellationToken ct = default)
    {
        ValidatePattern(pattern);
        if (!float.IsFinite(feedbackValue) || feedbackValue is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(feedbackValue));
        var copy = CopyPattern(pattern);
        return Task.Run(() =>
        {
            lock (gate)
            {
                ct.ThrowIfCancellationRequested();
                var branch = Branch(nodeId);
                var record = branch.Patterns.LastOrDefault(item => item.Feedback is null && SamePattern(item.Pattern, copy));
                if (record is null && branch.Patterns.Any(item => SamePattern(item.Pattern, copy)))
                    throw new InvalidOperationException("This recorded pattern has already been rated. Generate a new candidate before rating again.");
                // Compute on copies, then commit once: cancellation never leaves a half-trained matrix.
                var weights = Copy(branch.Weights);
                var density = Copy(branch.DensityWeights);
                float delta = Config.LearningRate * (feedbackValue - .5f) * 2;
                for (int step = 0; step < Config.TotalSteps; step++)
                {
                    ct.ThrowIfCancellationRequested();
                    int hits = copy.Count(row => row[step]);
                    for (int count = 0; count <= Config.MaximumHits; count++)
                        density[step][count] = Clamp(density[step][count] * (1 - Config.WeightDecay) + (count == hits ? delta : -delta / Config.MaximumHits));
                    for (int slot = 0; slot < Config.SampleCount; slot++)
                    {
                        float change = hits == 0 ? 0 : copy[slot][step] ? delta / hits :
                            hits < Config.SampleCount ? -delta / (Config.SampleCount - hits) : 0;
                        weights[step][slot] = Clamp(weights[step][slot] * (1 - Config.WeightDecay) + change);
                    }
                }
                ct.ThrowIfCancellationRequested();
                branch.Weights = weights;
                branch.DensityWeights = density;
                branch.FeedbackCount++;
                trainingIterations++;
                branch.Revision++;
                if (record is null) branch.Patterns.Add(new() { Pattern = copy, Revision = branch.Revision - 1, Feedback = feedbackValue });
                else record.Feedback = feedbackValue;
            }
        }, ct);
    }

    private static bool SamePattern(bool[][] left, bool[][] right) => left.Length == right.Length && left.Zip(right).All(pair => pair.First.SequenceEqual(pair.Second));
    private float Clamp(float value) => Math.Clamp(value, Config.MinWeight, Config.MaxWeight);
    private void ValidatePattern(bool[][] pattern)
    {
        if (pattern is null || pattern.Length != Config.SampleCount || pattern.Any(row => row is null || row.Length != Config.TotalSteps) ||
            Enumerable.Range(0, Config.TotalSteps).Any(step => pattern.Count(row => row[step]) > Config.MaximumHits))
            throw new ArgumentException("Pattern dimensions or simultaneous hit count do not match this session.", nameof(pattern));
    }

    public Task<string> CreateRemixNodeAsync(string parentNodeId, float mutationIntensity = .5f, CancellationToken ct = default) => Task.Run(() =>
    {
        lock (gate)
        {
            if (!float.IsFinite(mutationIntensity) || mutationIntensity is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(mutationIntensity));
            if (branches.Count >= 2048) throw new InvalidOperationException("Trim unused branches before adding more (2048 branch limit).");
            var parent = Branch(parentNodeId);
            var child = NewBranch("branch_" + Guid.NewGuid().ToString("N"), "Fork of " + parent.Name, parent.Id, mutationIntensity);
            child.Name = child.Name[..Math.Min(180, child.Name.Length)];
            child.Weights = Copy(parent.Weights);
            child.DensityWeights = Copy(parent.DensityWeights);
            foreach (var matrix in new[] { child.Weights, child.DensityWeights })
                foreach (var row in matrix)
                    for (int column = 0; column < row.Length; column++)
                    {
                        ct.ThrowIfCancellationRequested();
                        row[column] = Clamp(row[column] + ((float)Random.Shared.NextDouble() * 2 - 1) * mutationIntensity);
                    }
            branches.Add(child.Id, child);
            return child.Id;
        }
    }, ct);

    public string ForkPattern(string nodeId, string patternId)
    {
        lock (gate)
        {
            var parent = Branch(nodeId);
            var record = parent.Patterns.First(item => item.Id == patternId);
            if (branches.Count >= 2048) throw new InvalidOperationException("Branch limit reached.");
            var child = NewBranch("branch_" + Guid.NewGuid().ToString("N"), "Pattern fork", parent.Id, sourcePattern: patternId);
            // Explicit pattern seed, rather than pretending to reconstruct historical weights.
            for (int step = 0; step < Config.TotalSteps; step++)
            {
                int hits = record.Pattern.Count(row => row[step]);
                for (int slot = 0; slot < Config.SampleCount; slot++) child.Weights[step][slot] = Clamp(record.Pattern[slot][step] ? 2 : -2);
                for (int count = 0; count <= Config.MaximumHits; count++) child.DensityWeights[step][count] = Clamp(count == hits ? 2 : -2);
            }
            branches.Add(child.Id, child);
            return child.Id;
        }
    }

    public void SetWeight(string id, int step, int column, float value, bool density = false)
    {
        lock (gate)
        {
            var branch = Branch(id);
            var matrix = density ? branch.DensityWeights : branch.Weights;
            if (step < 0 || step >= matrix.Length || column < 0 || column >= matrix[step].Length || !float.IsFinite(value) || value < Config.MinWeight || value > Config.MaxWeight)
                throw new ArgumentOutOfRangeException(nameof(value), "Weight must be within this session's bounds.");
            matrix[step][column] = value;
            branch.Revision++;
            branch.ManualEditCount++;
        }
    }

    public void RenameBranch(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200) throw new ArgumentException("Use a branch name of 1–200 characters.");
        lock (gate) Branch(id).Name = name;
    }

    public void TrimBranch(string id)
    {
        lock (gate)
        {
            Branch(id);
            if (id == "root") throw new InvalidOperationException("The root cannot be trimmed. Reset the session instead.");
            var remove = new HashSet<string>(StringComparer.Ordinal) { id };
            bool added;
            do
            {
                added = false;
                foreach (var branch in branches.Values)
                    if (branch.ParentId is { } parent && remove.Contains(parent)) added |= remove.Add(branch.Id);
            } while (added);
            foreach (string key in remove) branches.Remove(key);
        }
    }

    public void Dispose() { lock (gate) { disposed = true; branches.Clear(); } }
}
