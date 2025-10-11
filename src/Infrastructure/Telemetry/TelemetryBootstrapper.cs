using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace Infrastructure.Telemetry;

/// <summary>
/// Sets up the Serilog pipeline that is shared by the application.
/// </summary>
public static class TelemetryBootstrapper
{
    private static readonly object SyncRoot = new();
    private static Logger? _logger;

    public static ILoggerFactory CreateLoggerFactory(TelemetryOptions options)
    {
        var serilogLogger = EnsureSerilogLogger(options);
        return new SerilogLoggerFactory(serilogLogger, dispose: false);
    }

    public static void Shutdown()
    {
        lock (SyncRoot)
        {
            _logger?.Dispose();
            _logger = null;
        }
    }

    public static Serilog.ILogger EnsureSerilogLogger(TelemetryOptions options)
    {
        lock (SyncRoot)
        {
            if (_logger is not null)
            {
                return _logger;
            }

            Directory.CreateDirectory(options.LogsDirectory);
            var logFilePath = options.GetLogFilePath();

            var configuration = new LoggerConfiguration()
                .MinimumLevel.Is(options.MinimumLogLevel)
                .Enrich.FromLogContext()
                .WriteTo.File(
                    logFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: options.RetainedLogFileCount,
                    shared: true,
                    restrictedToMinimumLevel: options.MinimumLogLevel);

            if (options.EnableDebugSink)
            {
                configuration = configuration.WriteTo.Debug();
            }

            _logger = configuration.CreateLogger();
            Log.Logger = _logger;
            return _logger;
        }
    }
}
