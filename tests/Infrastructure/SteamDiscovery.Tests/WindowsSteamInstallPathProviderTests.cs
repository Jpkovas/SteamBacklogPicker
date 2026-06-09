using FluentAssertions;
using Microsoft.Win32;
using SteamDiscovery;
using Xunit;

namespace SteamDiscovery.Tests;

#if WINDOWS
public sealed class WindowsSteamInstallPathProviderTests
{
    [Fact]
    public void WindowsProvider_ShouldUseSteamRegistryKeyPath()
    {
        var field = typeof(WindowsSteamInstallPathProvider).GetField(
            "SteamKeyPath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        field.Should().NotBeNull();
        field!.GetRawConstantValue().Should().Be(@"Software\Valve\Steam");
    }

    [Fact]
    public void WindowsProvider_ShouldReadCurrentUserSteamPath_WhenSteamRegistryKeyExists()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", writable: false);
        var expected = key?.GetValue("SteamPath") as string;
        if (string.IsNullOrWhiteSpace(expected))
        {
            return;
        }

        var result = new WindowsSteamInstallPathProvider().GetSteamInstallPath();

        result.Should().Be(expected);
    }
}
#endif
