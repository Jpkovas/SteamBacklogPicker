using System.Runtime.InteropServices;
using SteamBacklogPicker.Integration.SteamHooks;
using Xunit;

namespace SteamHooks.Tests;

public sealed class SteamHookClientFactoryTests
{
    [Fact]
    public void Create_MemoryModeWithoutLinuxFlag_ShouldDegradeOnLinux()
    {
        var diagnostics = new List<SteamHookDiagnostic>();
        var options = new SteamHookOptions
        {
            Mode = SteamHookMode.MemoryInspection,
            EnableUnsafeLinuxMemoryRead = false,
            DiagnosticListener = diagnostics.Add,
        };

        var client = SteamHookClientFactory.Create(options);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.Contains(diagnostics, d => d.EventName == "steam_hook_memory_mode_degraded");
            Assert.Equal("NullSteamHookClient", client.GetType().Name);
            return;
        }

        Assert.DoesNotContain(diagnostics, d => d.EventName == "steam_hook_memory_mode_degraded");
    }

    [Fact]
    public void Create_MemoryModeWithLinuxFlag_ShouldSelectMemoryClientOnLinuxOrWindows()
    {
        var options = new SteamHookOptions
        {
            Mode = SteamHookMode.MemoryInspection,
            EnableUnsafeLinuxMemoryRead = true,
        };

        var client = SteamHookClientFactory.Create(options);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.IsType<SteamMemoryPollingHookClient>(client);
            return;
        }

        Assert.NotNull(client);
    }
}
