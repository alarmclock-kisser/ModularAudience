using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModularAudience.Quantum.Abstractions;
using ModularAudience.Quantum.Models;
using ModularAudience.Quantum.Services;

namespace ModularAudience.Quantum.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers offline quantum patterns by default. No credentials or network access are required.</summary>
    public static IServiceCollection AddQuantumPatterns(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<LocalQuantumSimulatorOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IQuantumCircuitBuilder>(new OpenQasmCircuitBuilder());
        services.TryAddSingleton<IQuantumPatternMapper, BreakbeatPatternMapper>();
        services.TryAddSingleton<IQuantumPatternService, QuantumPatternService>();
        services.TryAddSingleton<IQuantumClient, LocalMatrixQuantumSimulatorClient>();
        return services;
    }

    public static IServiceCollection AddLocalQuantumSimulator(this IServiceCollection services,
        Action<LocalQuantumSimulatorOptions>? configure = null)
    {
        services.AddQuantumPatterns();
        if (configure is not null) services.Configure(configure);
        services.Replace(ServiceDescriptor.Singleton<IQuantumClient, LocalMatrixQuantumSimulatorClient>());
        return services;
    }

    /// <summary>Uses the IBM Sampler V2 transport. Supply backend-specific ISA QASM for execution on hardware.</summary>
    public static IServiceCollection AddIbmQuantum(this IServiceCollection services, Action<IbmQuantumOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.AddQuantumPatterns();
        services.Configure(configure);
        services.AddHttpClient(IbmQuantumClient.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient(IbmQuantumClient.IamHttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.Replace(ServiceDescriptor.Singleton<IQuantumClient, IbmQuantumClient>());
        return services;
    }
}
