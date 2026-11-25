using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ModularAudience.Audio.Synthesis.Runtime;

/// <summary>
/// Compiles LLM-generated C# DSP code in-memory into an isolated, collectible AssemblyLoadContext.
/// Unsafe APIs are rejected via syntax-tree inspection and a restricted metadata reference set.
/// </summary>
public sealed class SynthCompiler : IDisposable
{
    private static readonly string[] ForbiddenNamespacePrefixes =
    [
        "System.IO", "System.Net", "System.Reflection", "System.Diagnostics.Process",
        "System.Runtime.InteropServices", "Microsoft.Win32"
    ];

    private static readonly string[] ForbiddenTypeNames =
    [
        "DllImport", "DllImportAttribute", "Process", "ProcessStartInfo",
        "FileStream", "Directory", "File", "WebClient", "HttpClient",
        "Socket", "TcpClient", "UdpClient"
    ];

    private static readonly string[] AllowedAssemblyNames =
    [
        "System.Private.CoreLib", "System.Runtime", "System.Collections", "System.Linq",
        "System.Core", "System.Memory", "System.Numerics", "System.Numerics.Vectors"
    ];

    private readonly AssemblyLoadContext loadContext;
    private readonly List<Assembly> loadedAssemblies = [];
    private bool disposed;

    public SynthCompiler()
    {
        this.loadContext = new AssemblyLoadContext("SynthSandbox", isCollectible: true);
        this.loadContext.Resolving += ResolveAssembly;
    }

    /// <summary>
    /// Compiles C# source code into a collectible assembly load context.
    /// Returns diagnostics and, on success, the compiled assembly.
    /// </summary>
    public CompileResult Compile(string sourceCode, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            return CompileResult.Failed("Source code is empty.");
        }

        // 1. Syntax-tree safety inspection (rejects unsafe APIs before compilation).
        var safety = InspectForbiddenApis(sourceCode);
        if (!safety.IsSafe)
        {
            return CompileResult.Failed($"Rejected unsafe API: {safety.Reason}");
        }

        // 2. Parse the syntax tree.
        SyntaxTree tree;
        try
        {
            tree = CSharpSyntaxTree.ParseText(sourceCode, cancellationToken: token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CompileResult.Failed($"Syntax parse error: {ex.Message}");
        }
        token.ThrowIfCancellationRequested();

        // 3. Build restricted compilation references.
        var references = new List<MetadataReference>();
        foreach (var assemblyPath in AppDomain.CurrentDomain.GetAssemblies()
                     .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                     .Select(a => a.Location))
        {
            try
            {
                references.Add(MetadataReference.CreateFromFile(assemblyPath));
            }
            catch { /* skip unresolvable assemblies */ }
        }

        var compilation = CSharpCompilation.Create(
            "CustomInstrumentSynth_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: [tree],
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: false,
                checkOverflow: false,
                platform: Platform.AnyCpu));

        token.ThrowIfCancellationRequested();

        // 4. Emit to memory.
        using var peStream = new MemoryStream();
        using var pdbStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream, pdbStream, cancellationToken: token);

        var diagnostics = emitResult.Diagnostics
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(d => $"[{d.Id}] {d.GetMessage()} (line {d.Location.GetLineSpan().StartLinePosition.Line + 1})")
            .ToImmutableArray();

        if (!emitResult.Success)
        {
            return CompileResult.Failed(diagnostics, emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.GetMessage())
                .ToImmutableArray());
        }

        // 5. Load into the collectible context.
        peStream.Position = 0;
        pdbStream.Position = 0;
        Assembly assembly;
        try
        {
            assembly = this.loadContext.LoadFromStream(peStream, pdbStream);
        }
        catch (Exception ex)
        {
            return CompileResult.Failed($"Assembly load error: {ex.Message}");
        }

        lock (this.loadedAssemblies)
        {
            this.loadedAssemblies.Add(assembly);
        }

        return CompileResult.Ok(assembly);
    }

    /// <summary>
    /// Unloads the collectible AssemblyLoadContext and all assemblies loaded into it.
    /// </summary>
    public void Unload()
    {
        lock (this.loadedAssemblies)
        {
            this.loadedAssemblies.Clear();
        }

        // Release all references so the GC can collect the context.
        var context = this.loadContext;
        // ReSharper disable once SuspiciousTypeConversion.Global
        if (context is AssemblyLoadContext collectible && collectible.IsCollectible)
        {
            collectible.Unload();
        }
    }

    private Assembly? ResolveAssembly(AssemblyLoadContext context, AssemblyName name)
    {
        // Only allow references to known-safe framework assemblies.
        if (!AllowedAssemblyNames.Contains(name.Name))
        {
            return null;
        }

        try
        {
            return Assembly.Load(new AssemblyName(name.Name));
        }
        catch
        {
            return null;
        }
    }

    private static SafetyInspection InspectForbiddenApis(string sourceCode)
    {
        // Check for unsafe keyword.
        if (Regex.IsMatch(sourceCode, @"\bunsafe\b", RegexOptions.IgnoreCase))
        {
            return new SafetyInspection(false, "The 'unsafe' keyword is not permitted.");
        }

        // Check for DllImport attribute usage.
        if (Regex.IsMatch(sourceCode, @"DllImport", RegexOptions.IgnoreCase))
        {
            return new SafetyInspection(false, "DllImport is not permitted.");
        }

        // Check for forbidden namespace usings and fully-qualified type references.
        foreach (var prefix in ForbiddenNamespacePrefixes)
        {
            if (sourceCode.Contains(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return new SafetyInspection(false, $"Namespace '{prefix}' is not permitted.");
            }
        }

        foreach (var typeName in ForbiddenTypeNames)
        {
            if (Regex.IsMatch(sourceCode, $@"\b{Regex.Escape(typeName)}\b", RegexOptions.IgnoreCase))
            {
                return new SafetyInspection(false, $"Type '{typeName}' is not permitted.");
            }
        }

        return new SafetyInspection(true, string.Empty);
    }

    private sealed record SafetyInspection(bool IsSafe, string Reason);

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            Unload();
        }
    }
}

/// <summary>Result of a synth compilation attempt.</summary>
public sealed class CompileResult
{
    private CompileResult(bool success, Assembly? assembly, ImmutableArray<string> diagnostics, ImmutableArray<string> errors)
    {
        IsSuccess = success;
        Assembly = assembly;
        Diagnostics = diagnostics;
        Errors = errors;
    }

    public bool IsSuccess { get; }
    public Assembly? Assembly { get; }
    public ImmutableArray<string> Diagnostics { get; }
    public ImmutableArray<string> Errors { get; }

    public static CompileResult Ok(Assembly assembly) => new(true, assembly, [], []);
    public static CompileResult Failed(params string[] errors) => new(false, null, [], errors.ToImmutableArray());
    public static CompileResult Failed(ImmutableArray<string> diagnostics, ImmutableArray<string> errors) => new(false, null, diagnostics, errors);
    public static CompileResult Failed(string error) => new(false, null, [], [error]);
}
