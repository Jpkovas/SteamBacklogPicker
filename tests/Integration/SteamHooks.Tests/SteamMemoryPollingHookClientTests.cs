using System.Diagnostics;
using System.Reflection;
using SteamBacklogPicker.Integration.SteamHooks;
using Xunit;

namespace SteamHooks.Tests;

public sealed class SteamMemoryPollingHookClientTests
{
    [Fact]
    public async Task TryCaptureDownloadState_DisposesProcessWhenAccessorCreationFails()
    {
        // Arrange
        var options = new SteamHookOptions();
        var process = Process.GetCurrentProcess();
        await using var client = new SteamMemoryPollingHookClient(
            options,
            _ => process,
            _ => null);

        var method = typeof(SteamMemoryPollingHookClient).GetMethod(
            "TryCaptureDownloadState",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var parameters = new object?[] { null };

        // Act
        var result = (bool)method!.Invoke(client, parameters)!;

        // Assert
        Assert.False(result);
        Assert.True(process.SafeHandle.IsClosed);
    }
}
