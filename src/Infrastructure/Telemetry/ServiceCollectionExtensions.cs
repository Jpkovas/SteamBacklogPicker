using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Extensions.Logging;

namespace Infrastructure.Telemetry;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTelemetryInfrastructure(this IServiceCollection services, Action<TelemetryOptions>? configure = null)
    {
        var options = new TelemetryOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<ITelemetryConsentStore, FileTelemetryConsentStore>();
        services.AddSingleton<ITelemetryConsentService, TelemetryConsentService>();
        services.AddSingleton<Serilog.ILogger>(sp => TelemetryBootstrapper.EnsureSerilogLogger(sp.GetRequiredService<TelemetryOptions>()));
        services.AddSingleton<ITelemetryClient, SerilogTelemetryClient>();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(TelemetryBootstrapper.EnsureSerilogLogger(options), dispose: false);
        });

        return services;
    }
}
