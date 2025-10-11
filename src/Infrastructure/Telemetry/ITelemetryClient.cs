namespace Infrastructure.Telemetry;

public interface ITelemetryClient
{
    void TrackEvent(string eventName, IReadOnlyDictionary<string, object>? properties = null);

    void TrackException(Exception exception, IReadOnlyDictionary<string, object>? properties = null);
}
