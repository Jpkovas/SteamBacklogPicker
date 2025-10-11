namespace Infrastructure.Telemetry;

public interface ITelemetryConsentService
{
    bool HasResponded { get; }

    bool IsTelemetryEnabled { get; }

    void SetTelemetryEnabled(bool enabled);
}
