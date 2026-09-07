using Infrastructure.Telemetry;
using FluentAssertions;
using Xunit;

namespace SteamBacklogPicker.Linux.Tests;

public sealed class TelemetryResilienceTests
{
    [Fact]
    public void OptionalTelemetry_ShouldTolerateUnwritableDirectoriesAndRequireExplicitConsent()
    {
        var path = Path.Combine(Path.GetTempPath(), "sbp-telemetry-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "A regular file cannot be used as a directory.");
        try
        {
            var options = new TelemetryOptions
            {
                LogsDirectory = path,
                TelemetryStoreDirectory = path,
                TelemetryEnabledByDefault = true
            };
            TelemetryBootstrapper.Shutdown();
            using var logger = TelemetryBootstrapper.CreateLoggerFactory(options);
            var consent = new TelemetryConsentService(new FileTelemetryConsentStore(options), options);
            consent.HasResponded.Should().BeFalse();
            consent.IsTelemetryEnabled.Should().BeFalse();
            consent.SetTelemetryEnabled(true);
            consent.IsTelemetryEnabled.Should().BeTrue();
            consent.SetTelemetryEnabled(false);
            consent.IsTelemetryEnabled.Should().BeFalse();
        }
        finally
        {
            TelemetryBootstrapper.Shutdown();
            File.Delete(path);
        }
    }
}
