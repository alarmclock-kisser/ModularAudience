using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ModularAudience.Generators
{
    public record BeatEngineConfig
    {
        public int SampleCount { get; init; } = 8;
        public int Bars { get; init; } = 1;                  // z. B. 1, 2, 4 Takte
        public int BeatsPerBar { get; init; } = 4;           // z. B. 4/4, 3/4, 7/8 Takt
        public int StepsPerBeat { get; init; } = 4;          // 4 = 16tel, 8 = 32stel, 6 = Triolen

        public float LearningRate { get; init; } = 0.2f;     // Alpha
        public float Temperature { get; init; } = 1.0f;      // Softmax Temperature
        public float WeightDecay { get; init; } = 0.01f;     // Evaporation rate
        public float MinWeight { get; init; } = -10.0f;
        public float MaxWeight { get; init; } = 10.0f;

        public int ThreadCount { get; init; } = Environment.ProcessorCount;
        public int Interleaved { get; init; } = 1;           // Max. zusätzliche Noten pro Step

        public int TotalSteps => this.Bars * this.BeatsPerBar * this.StepsPerBeat;
    }

    public class BeatNode : IDisposable
    {
        public string Id { get; }
        public float[,] Weights { get; } // [Step, SampleIndex]
        private readonly ReaderWriterLockSlim _lock = new();

        public BeatNode(string id, int totalSteps, int sampleCount)
        {
            this.Id = id;
            this.Weights = new float[totalSteps, sampleCount];
        }

        public void ReadLock(Action action)
        {
            this._lock.EnterReadLock();
            try { action(); }
            finally { this._lock.ExitReadLock(); }
        }

        public T ReadLock<T>(Func<T> func)
        {
            this._lock.EnterReadLock();
            try { return func(); }
            finally { this._lock.ExitReadLock(); }
        }

        public void WriteLock(Action action)
        {
            this._lock.EnterWriteLock();
            try { action(); }
            finally { this._lock.ExitWriteLock(); }
        }

        public void Dispose() => this._lock.Dispose();
    }

    public class AsyncBeatGraphEngine
    {
        private readonly BeatEngineConfig _config;
        private readonly ConcurrentDictionary<string, BeatNode> _nodes = new();
        private readonly ThreadLocal<Random> _asyncRng = new(() => new Random(Guid.NewGuid().GetHashCode()));

        public AsyncBeatGraphEngine(BeatEngineConfig config)
        {
            this._config = config;
            this.CreateNode("root");
        }

        public BeatNode CreateNode(string nodeId)
        {
            var node = new BeatNode(nodeId, this._config.TotalSteps, this._config.SampleCount);
            this._nodes[nodeId] = node;
            return node;
        }

        /// <summary>
        /// Generiert eine Sequence asynchron und Thread-sicher via Softmax Sampling.
        /// </summary>
        public Task<int[]> GenerateSequenceAsync(string nodeId, float? temperatureOverride = null, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (!this._nodes.TryGetValue(nodeId, out var node))
                    throw new KeyNotFoundException($"Node {nodeId} nicht gefunden.");

                float temp = Math.Max(0.01f, temperatureOverride ?? this._config.Temperature);
                int[] sequence = new int[this._config.TotalSteps];
                var rng = this._asyncRng.Value!;

                node.ReadLock(() =>
                {
                    for (int step = 0; step < this._config.TotalSteps; step++)
                    {
                        ct.ThrowIfCancellationRequested();

                        // 1. Logits extrahieren
                        float[] logits = new float[this._config.SampleCount];
                        for (int s = 0; s < this._config.SampleCount; s++)
                        {
                            logits[s] = node.Weights[step, s] / temp;
                        }

                        // 2. Numerisch stabile Softmax-Transformation
                        float maxLogit = logits.Max();
                        float[] expValues = logits.Select(l => MathF.Exp(l - maxLogit)).ToArray();
                        float sumExp = expValues.Sum();

                        // 3. Stochastisches Sampling
                        float roll = (float) rng.NextDouble() * sumExp;
                        float current = 0f;
                        int selected = 0;

                        for (int s = 0; s < this._config.SampleCount; s++)
                        {
                            current += expValues[s];
                            if (roll <= current)
                            {
                                selected = s;
                                break;
                            }
                        }
                        sequence[step] = selected;
                    }
                });

                return sequence;
            }, ct);
        }

        /// <summary>
        /// Generiert ein Pattern als [SampleIndex][Step]. Interleaved begrenzt zusätzliche Noten pro Step.
        /// </summary>
        public Task<bool[][]> GeneratePatternAsync(string nodeId, float? temperatureOverride = null, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (!this._nodes.TryGetValue(nodeId, out var node))
                    throw new KeyNotFoundException($"Node {nodeId} nicht gefunden.");

                float temp = Math.Max(0.01f, temperatureOverride ?? this._config.Temperature);
                int maxAdditionalNotes = Math.Clamp(this._config.Interleaved, 0, Math.Max(0, this._config.SampleCount - 1));
                bool[][] pattern = Enumerable.Range(0, this._config.SampleCount)
                    .Select(_ => new bool[this._config.TotalSteps])
                    .ToArray();
                var rng = this._asyncRng.Value!;

                node.ReadLock(() =>
                {
                    for (int step = 0; step < this._config.TotalSteps; step++)
                    {
                        ct.ThrowIfCancellationRequested();

                        float[] expValues = new float[this._config.SampleCount];
                        float maxLogit = float.NegativeInfinity;
                        for (int sample = 0; sample < this._config.SampleCount; sample++)
                        {
                            maxLogit = Math.Max(maxLogit, node.Weights[step, sample] / temp);
                        }

                        float sumExp = 0f;
                        for (int sample = 0; sample < this._config.SampleCount; sample++)
                        {
                            expValues[sample] = MathF.Exp(node.Weights[step, sample] / temp - maxLogit);
                            sumExp += expValues[sample];
                        }

                        var candidates = Enumerable.Range(0, this._config.SampleCount).ToList();
                        int noteCount = 1 + (maxAdditionalNotes == 0 ? 0 : rng.Next(maxAdditionalNotes + 1));
                        for (int note = 0; note < noteCount && candidates.Count > 0; note++)
                        {
                            float candidateWeight = candidates.Sum(index => expValues[index]);
                            float roll = (float)rng.NextDouble() * candidateWeight;
                            float current = 0f;
                            int selected = candidates[^1];
                            foreach (int candidate in candidates)
                            {
                                current += expValues[candidate];
                                if (roll <= current)
                                {
                                    selected = candidate;
                                    break;
                                }
                            }

                            pattern[selected][step] = true;
                            candidates.Remove(selected);
                        }
                    }
                });

                return pattern;
            }, ct);
        }

        /// <summary>
        /// Generiert mehrere Sequenzen parallel über konfigurierte Threads.
        /// </summary>
        public async Task<List<int[]>> GenerateBatchAsync(string nodeId, int batchSize, CancellationToken ct = default)
        {
            var results = new ConcurrentBag<int[]>();
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = this._config.ThreadCount,
                CancellationToken = ct
            };

            await Parallel.ForEachAsync(Enumerable.Range(0, batchSize), parallelOptions, async (_, token) =>
            {
                var seq = await this.GenerateSequenceAsync(nodeId, null, token);
                results.Add(seq);
            });

            return results.ToList();
        }

        /// <summary>
        /// Wendet Feedback auf ein Pattern mit einer oder mehreren Noten pro Step an.
        /// </summary>
        public Task ApplyFeedbackAsync(string nodeId, bool[][] pattern, float feedbackValue, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (!this._nodes.TryGetValue(nodeId, out var node))
                    throw new KeyNotFoundException($"Node {nodeId} nicht gefunden.");
                if (pattern.Length != this._config.SampleCount || pattern.Any(row => row.Length != this._config.TotalSteps))
                    throw new ArgumentException("Pattern dimensions do not match the engine configuration.", nameof(pattern));

                float rewardSignal = Math.Clamp((feedbackValue - 0.5f) * 2.0f, -1.0f, 1.0f);
                float delta = this._config.LearningRate * rewardSignal;
                node.WriteLock(() =>
                {
                    for (int step = 0; step < this._config.TotalSteps; step++)
                    {
                        ct.ThrowIfCancellationRequested();
                        int playedCount = pattern.Count(row => row[step]);
                        int unplayedCount = this._config.SampleCount - playedCount;
                        float offPathPenalty = unplayedCount > 0 ? delta * Math.Max(1, playedCount) / unplayedCount : 0f;

                        for (int sample = 0; sample < this._config.SampleCount; sample++)
                        {
                            float weight = node.Weights[step, sample] * (1.0f - this._config.WeightDecay);
                            weight += pattern[sample][step] ? delta : -offPathPenalty;
                            node.Weights[step, sample] = Math.Clamp(weight, this._config.MinWeight, this._config.MaxWeight);
                        }
                    }
                });
            }, ct);
        }

        /// <summary>
        /// Wendet kontinuierliches Feedback (0.0 bis 1.0) an.
        /// 0.5 = Neutral, >0.5 = Belohnung, <0.5 = Bestrafung.
        /// </summary>
        public Task ApplyFeedbackAsync(string nodeId, int[] sequence, float feedbackValue, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (!this._nodes.TryGetValue(nodeId, out var node))
                    throw new KeyNotFoundException($"Node {nodeId} nicht gefunden.");

                // Scaling: Map 0.0..1.0 auf -1.0 .. +1.0
                float rewardSignal = Math.Clamp((feedbackValue - 0.5f) * 2.0f, -1.0f, 1.0f);
                float alpha = this._config.LearningRate;

                node.WriteLock(() =>
                {
                    for (int step = 0; step < this._config.TotalSteps; step++)
                    {
                        ct.ThrowIfCancellationRequested();
                        int playedSample = sequence[step];

                        // 1. Evaporation / Decay auf alle Gewichte anwenden
                        for (int s = 0; s < this._config.SampleCount; s++)
                        {
                            node.Weights[step, s] *= (1.0f - this._config.WeightDecay);
                        }

                        // 2. Belohnung/Bestrafung für den gespielten Pfad
                        float currentW = node.Weights[step, playedSample];
                        float delta = alpha * rewardSignal;

                        // Gegen-Gewichtung für nicht gespielte Pfade (Normalisierungskompensation)
                        float offPathPenalty = (delta / (this._config.SampleCount - 1));

                        for (int s = 0; s < this._config.SampleCount; s++)
                        {
                            if (s == playedSample)
                            {
                                node.Weights[step, s] = Math.Clamp(currentW + delta, this._config.MinWeight, this._config.MaxWeight);
                            }
                            else
                            {
                                node.Weights[step, s] = Math.Clamp(node.Weights[step, s] - offPathPenalty, this._config.MinWeight, this._config.MaxWeight);
                            }
                        }
                    }
                });
            }, ct);
        }

        /// <summary>
        /// Erzeugt ein mutiertes Derivat (Remix-Knoten) asynchron.
        /// </summary>
        public Task<string> CreateRemixNodeAsync(string parentNodeId, float mutationIntensity = 0.5f, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (!this._nodes.TryGetValue(parentNodeId, out var parentNode))
                    throw new KeyNotFoundException($"Parent-Node {parentNodeId} nicht gefunden.");

                string childId = $"remix_{Guid.NewGuid().ToString()[..8]}";
                var childNode = this.CreateNode(childId);
                var rng = this._asyncRng.Value!;

                parentNode.ReadLock(() =>
                {
                    childNode.WriteLock(() =>
                    {
                        for (int step = 0; step < this._config.TotalSteps; step++)
                        {
                            ct.ThrowIfCancellationRequested();
                            for (int s = 0; s < this._config.SampleCount; s++)
                            {
                                // Gaussian / Uniform Rauschen injizieren
                                float noise = ((float) rng.NextDouble() * 2f - 1f) * mutationIntensity;
                                float inheritedWeight = parentNode.Weights[step, s];

                                childNode.Weights[step, s] = Math.Clamp(inheritedWeight + noise, this._config.MinWeight, this._config.MaxWeight);
                            }
                        }
                    });
                });

                return childId;
            }, ct);
        }
    }
}