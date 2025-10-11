using System.Globalization;

namespace Infrastructure.Telemetry;

/// <summary>
/// Represents configuration used when bootstrapping logging and telemetry.
/// </summary>
public sealed class TelemetryOptions
{
    /// <summary>
    /// Gets or sets the friendly name of the application. Used in log file names and telemetry payloads.
    /// </summary>
    public string ApplicationName { get; set; } = "SteamBacklogPicker";

    /// <summary>
    /// Gets or sets the directory where log files should be written.
    /// </summary>
    public string LogsDirectory { get; set; } = DefaultLogsDirectory;

    /// <summary>
    /// Gets or sets a value indicating whether debug logging should also be written to the debugger.
    /// </summary>
    public bool EnableDebugSink { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether telemetry features are enabled by default.
    /// Users can still opt out by using the consent service.
    /// </summary>
    public bool TelemetryEnabledByDefault { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of log files to retain.
    /// </summary>
    public int RetainedLogFileCount { get; set; } = 7;

    /// <summary>
    /// Gets or sets the minimum level that should be captured by the Serilog pipeline.
    /// </summary>
    public Serilog.Events.LogEventLevel MinimumLogLevel { get; set; } = Serilog.Events.LogEventLevel.Information;

    /// <summary>
    /// Gets or sets the directory where telemetry opt-in state is stored.
    /// </summary>
    public string TelemetryStoreDirectory { get; set; } = DefaultDataDirectory;

    /// <summary>
    /// Gets or sets an optional endpoint to which telemetry events may be forwarded.
    /// </summary>
    public Uri? TelemetryEndpoint { get; set; }

    public static string DefaultLogsDirectory => Path.Combine(DefaultDataDirectory, "logs");

    public static string DefaultDataDirectory
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Path.GetTempPath();
            }

            return Path.Combine(appData, ApplicationFolderName);
        }
    }

    public static string ApplicationFolderName => "SteamBacklogPicker";

    public string GetLogFilePath()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return Path.Combine(LogsDirectory, $"{ApplicationName}-{timestamp}.log");
    }
}
