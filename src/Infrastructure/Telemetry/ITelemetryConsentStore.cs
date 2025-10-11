namespace Infrastructure.Telemetry;

/// <summary>
/// Provides storage for telemetry consent preferences.
/// </summary>
public interface ITelemetryConsentStore
{
    TelemetryConsentState Load();

    void Save(TelemetryConsentState state);
}
