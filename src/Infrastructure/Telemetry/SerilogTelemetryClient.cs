using Serilog;

namespace Infrastructure.Telemetry;

/// <summary>
/// Forwards telemetry data to the shared Serilog pipeline when consent allows it.
/// </summary>
public sealed class SerilogTelemetryClient : ITelemetryClient
{
    private readonly ITelemetryConsentService _consentService;
    private readonly ILogger _logger;

    public SerilogTelemetryClient(ITelemetryConsentService consentService, ILogger logger)
    {
        _consentService = consentService;
        _logger = logger;
    }

    public void TrackEvent(string eventName, IReadOnlyDictionary<string, object>? properties = null)
    {
        if (!_consentService.IsTelemetryEnabled)
        {
            return;
        }

        _logger.Information("Telemetry event {EventName} {@Properties}", eventName, properties);
    }

    public void TrackException(Exception exception, IReadOnlyDictionary<string, object>? properties = null)
    {
        if (!_consentService.IsTelemetryEnabled)
        {
            return;
        }

        _logger.Error(exception, "Telemetry exception {@Properties}", properties);
    }
}
