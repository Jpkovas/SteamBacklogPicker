namespace Infrastructure.Telemetry;

/// <summary>
/// Represents persisted telemetry consent state for a user.
/// </summary>
public sealed class TelemetryConsentState
{
    public bool HasResponded { get; init; }

    public bool IsTelemetryEnabled { get; init; }

    public static TelemetryConsentState CreateDefault(bool enabledByDefault) => new()
    {
        HasResponded = enabledByDefault,
        IsTelemetryEnabled = enabledByDefault
    };
}
