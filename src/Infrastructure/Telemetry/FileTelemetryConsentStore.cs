using System.Text.Json;

namespace Infrastructure.Telemetry;

/// <summary>
/// Persists consent state to a JSON file in the user's profile.
/// </summary>
public sealed class FileTelemetryConsentStore : ITelemetryConsentStore
{
    private readonly string _storePath;
    private readonly TelemetryOptions _options;

    public FileTelemetryConsentStore(TelemetryOptions options)
    {
        _options = options;
        Directory.CreateDirectory(options.TelemetryStoreDirectory);
        _storePath = Path.Combine(options.TelemetryStoreDirectory, "telemetry-consent.json");
    }

    public TelemetryConsentState Load()
    {
        if (!File.Exists(_storePath))
        {
            return TelemetryConsentState.CreateDefault(_options.TelemetryEnabledByDefault);
        }

        try
        {
            using var stream = File.OpenRead(_storePath);
            TelemetryConsentState? state = JsonSerializer.Deserialize<TelemetryConsentState>(stream);
            return state ?? TelemetryConsentState.CreateDefault(_options.TelemetryEnabledByDefault);
        }
        catch
        {
            return TelemetryConsentState.CreateDefault(_options.TelemetryEnabledByDefault);
        }
    }

    public void Save(TelemetryConsentState state)
    {
        var directory = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(_storePath);
        JsonSerializer.Serialize(stream, state, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
