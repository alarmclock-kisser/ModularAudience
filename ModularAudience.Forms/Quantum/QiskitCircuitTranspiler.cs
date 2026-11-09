using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModularAudience.Quantum.Models;

namespace ModularAudience.Forms.Quantum;

internal interface IQuantumCircuitTranspiler
{
    Task<string> TranspileAsync(string logicalQasm, int logicalQubits, QuantumBackendConfiguration target,
        string pythonExecutable, int optimizationLevel, int seed, CancellationToken cancellationToken);
}

/// <summary>Local Qiskit process. Only circuit/target metadata crosses stdin; no IBM credentials are passed.</summary>
internal sealed class QiskitCircuitTranspiler : IQuantumCircuitTranspiler
{
    public async Task<string> TranspileAsync(string logicalQasm, int logicalQubits, QuantumBackendConfiguration target,
        string pythonExecutable, int optimizationLevel, int seed, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pythonExecutable);
        if (optimizationLevel is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(optimizationLevel));
        using var resource = typeof(QiskitCircuitTranspiler).Assembly.GetManifestResourceStream("ModularAudience.Quantum.TranspileCircuit.py")
            ?? throw new InvalidOperationException("The embedded Qiskit transpiler is unavailable.");
        using var reader = new StreamReader(resource);
        string script = await reader.ReadToEndAsync(cancellationToken);
        var startInfo = new ProcessStartInfo(pythonExecutable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-I"); startInfo.ArgumentList.Add("-u"); startInfo.ArgumentList.Add("-c"); startInfo.ArgumentList.Add(script);
        string input = JsonSerializer.Serialize(new TranspilerInput(logicalQasm, logicalQubits, target, optimizationLevel, seed),
            QuantumStudioJsonContext.Default.TranspilerInput);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start()) throw new InvalidOperationException("The configured Python executable could not be started.");
            try
            {
                Task<string> output = ReadBoundedAsync(process.StandardOutput, 1_048_576, linked.Token);
                Task<string> errors = ReadBoundedAsync(process.StandardError, 65_536, linked.Token);
                await process.StandardInput.WriteAsync(input.AsMemory(), linked.Token);
                process.StandardInput.Close();
                await process.WaitForExitAsync(linked.Token);
                string json = await output;
                _ = await errors; // Drained, but never displayed or logged verbatim.
                if (process.ExitCode != 0) throw new InvalidOperationException("Qiskit transpilation failed. Check the Python environment, circuit and backend target.");
                using var result = JsonDocument.Parse(json);
                if (result.RootElement.TryGetProperty("error", out var error))
                    throw new InvalidOperationException(error.GetString() == "qiskit_missing"
                        ? "Qiskit is not installed in the selected Python environment. Open Help > IBM hardware setup."
                        : "Qiskit could not transpile the circuit for this backend. Check the gate set and target connectivity.");
                string qasm = result.RootElement.GetProperty("qasm").GetString() ?? "";
                if (!qasm.StartsWith("OPENQASM 3.0;", StringComparison.Ordinal) || qasm.Length > 1_048_576)
                    throw new InvalidOperationException("Qiskit returned an invalid hardware circuit.");
                return qasm;
            }
            finally
            {
                try { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); } }
                catch (InvalidOperationException) { }
            }
        }
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("Local Qiskit transpilation timed out. No quantum job was submitted.", ex); }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximum, CancellationToken token)
    {
        var output = new System.Text.StringBuilder();
        char[] buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            if (output.Length + count > maximum) throw new InvalidOperationException("The local transpiler output exceeded its safety limit.");
            output.Append(buffer, 0, count);
        }
        return output.ToString();
    }
}

internal sealed record TranspilerInput(string Qasm, int LogicalQubits, QuantumBackendConfiguration Target, int OptimizationLevel, int Seed);

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(TranspilerInput))]
[JsonSerializable(typeof(QuantumStudioWorkspace))]
[JsonSerializable(typeof(StoredQuantumExperiment))]
[JsonSerializable(typeof(QuantumConnectionDocument))]
internal partial class QuantumStudioJsonContext : JsonSerializerContext;
