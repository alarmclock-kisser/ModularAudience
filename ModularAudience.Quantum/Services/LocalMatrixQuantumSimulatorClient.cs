using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ModularAudience.Quantum.Abstractions;
using ModularAudience.Quantum.Models;

namespace ModularAudience.Quantum.Services;

/// <summary>Offline state-vector simulation of the builder's H, X, CX and Ry subset (4 to 16 qubits).</summary>
public sealed partial class LocalMatrixQuantumSimulatorClient(IOptions<LocalQuantumSimulatorOptions> options) : IQuantumClient
{
    public Task<QuantumJobResponse> ExecuteAsync(QuantumJobRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        int? seed = options.Value.Seed;
        long start = Stopwatch.GetTimestamp();
        return Task.Run(() => Simulate(request, seed, start, cancellationToken), cancellationToken);
    }

    private static QuantumJobResponse Simulate(QuantumJobRequest request, int? seed, long start, CancellationToken token)
    {
        string[] statements = request.CircuitQasm.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool version2 = statements.Length >= 5 && statements[0] == "OPENQASM 2.0";
        string count = request.QubitCount.ToString(CultureInfo.InvariantCulture);
        string[] prefix = version2
            ? ["OPENQASM 2.0", "include\"qelib1.inc\"", $"qregq[{count}]", $"cregc[{count}]"]
            : ["OPENQASM 3.0", "include\"stdgates.inc\"", $"qubit[{count}]q", $"bit[{count}]c"];
        if (request.ClassicalRegister != "c" || statements.Length < 5 ||
            !prefix.Select(RemoveWhitespace).SequenceEqual(statements.Take(4).Select(RemoveWhitespace)) ||
            RemoveWhitespace(statements[^1]) != (version2 ? "measureq->c" : "c=measureq"))
            throw new NotSupportedException("The local simulator requires the builder's QASM 2/3 registers and one final full-register measurement.");

        var amplitudes = new double[1 << request.QubitCount];
        amplitudes[0] = 1;
        foreach (string statement in statements.Skip(4).Take(statements.Length - 5))
        {
            token.ThrowIfCancellationRequested();
            Match match = Gate().Match(statement);
            if (!match.Success) throw new NotSupportedException("The local simulator only supports H, X, CX and finite numeric Ry gates.");
            string gate = match.Groups["gate"].Value;
            if (!int.TryParse(match.Groups["first"].Value, CultureInfo.InvariantCulture, out int first) || first >= request.QubitCount)
                throw new ArgumentException("A gate references a qubit outside the circuit.");
            bool hasSecond = match.Groups["second"].Success;
            bool hasAngle = match.Groups["theta"].Success;
            if ((gate == "cx") != hasSecond || (gate == "ry") != hasAngle)
                throw new ArgumentException("A local gate has an invalid number of arguments.");
            int second = 0;
            if (hasSecond && (!int.TryParse(match.Groups["second"].Value, CultureInfo.InvariantCulture, out second) ||
                second >= request.QubitCount || second == first))
                throw new ArgumentException("CNOT requires two distinct qubits inside the circuit.");
            double theta = 0;
            if (hasAngle && (!double.TryParse(match.Groups["theta"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out theta) || !double.IsFinite(theta)))
                throw new ArgumentException("Ry angles must be finite numeric radians.");
            if (gate == "cx") ApplyCnot(amplitudes, first, second, token);
            else
            {
                double cosine = Math.Cos(theta / 2), sine = Math.Sin(theta / 2), h = Math.Sqrt(0.5);
                (double a, double b, double c, double d) = gate switch
                {
                    "h" => (h, h, h, -h),
                    "x" => (0, 1, 1, 0),
                    _ => (cosine, -sine, sine, cosine)
                };
                ApplySingleQubit(amplitudes, first, a, b, c, d, token);
            }
        }

        double total = 0;
        for (int i = 0; i < amplitudes.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            total += amplitudes[i] * amplitudes[i];
            amplitudes[i] = total;
        }
        var random = seed.HasValue ? new Random(seed.Value) : new Random(Random.Shared.Next());
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int shot = 0; shot < request.Shots; shot++)
        {
            if ((shot & 255) == 0) token.ThrowIfCancellationRequested();
            double draw = random.NextDouble() * total;
            int low = 0, high = amplitudes.Length - 1;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (draw < amplitudes[middle]) high = middle;
                else low = middle + 1;
            }
            string bits = Convert.ToString(low, 2).PadLeft(request.QubitCount, '0');
            counts[bits] = counts.GetValueOrDefault(bits) + 1;
        }
        token.ThrowIfCancellationRequested();
        return new("local-" + Guid.NewGuid().ToString("N"), QuantumCounts.Snapshot(counts), Stopwatch.GetElapsedTime(start));
    }

    private static string RemoveWhitespace(string text) => string.Concat(text.Where(character => !char.IsWhiteSpace(character)));

    private static void ApplySingleQubit(double[] state, int qubit, double a, double b, double c, double d, CancellationToken token)
    {
        int mask = 1 << qubit;
        for (int i = 0; i < state.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            if ((i & mask) != 0) continue;
            int other = i | mask;
            (state[i], state[other]) = (a * state[i] + b * state[other], c * state[i] + d * state[other]);
        }
    }

    private static void ApplyCnot(double[] state, int control, int target, CancellationToken token)
    {
        int controlMask = 1 << control, targetMask = 1 << target;
        for (int i = 0; i < state.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            if ((i & controlMask) == 0 || (i & targetMask) != 0) continue;
            int other = i | targetMask;
            (state[i], state[other]) = (state[other], state[i]);
        }
    }

    [GeneratedRegex(@"^(?<gate>h|x|cx|ry)(?:\((?<theta>[^)]+)\))?\s+q\[(?<first>\d+)\](?:,\s*q\[(?<second>\d+)\])?$", RegexOptions.CultureInvariant)]
    private static partial Regex Gate();
}
