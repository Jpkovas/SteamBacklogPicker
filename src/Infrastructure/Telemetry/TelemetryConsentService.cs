namespace Infrastructure.Telemetry;

public sealed class TelemetryConsentService : ITelemetryConsentService
{
    private readonly ITelemetryConsentStore _store;
    private readonly object _syncRoot = new();
    private TelemetryConsentState _state;

    public TelemetryConsentService(ITelemetryConsentStore store, TelemetryOptions options)
    {
        _store = store;
        _state = store.Load();

        if (!_state.HasResponded && options.TelemetryEnabledByDefault)
        {
            SetTelemetryEnabled(true);
        }
    }

    public bool HasResponded
    {
        get
        {
            lock (_syncRoot)
            {
                return _state.HasResponded;
            }
        }
    }

    public bool IsTelemetryEnabled
    {
        get
        {
            lock (_syncRoot)
            {
                return _state.IsTelemetryEnabled;
            }
        }
    }

    public void SetTelemetryEnabled(bool enabled)
    {
        lock (_syncRoot)
        {
            _state = new TelemetryConsentState
            {
                HasResponded = true,
                IsTelemetryEnabled = enabled
            };
            _store.Save(_state);
        }
    }
}
