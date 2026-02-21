using System;
using System.IO;
using System.Runtime.InteropServices;
using FluentAssertions;
using SteamBacklogPicker.UI.Services.Runtime;
using Xunit;

namespace SteamBacklogPicker.UI.Tests;

public sealed class SteamNativeLibraryCandidatesTests
{
    [Fact]
    public void Build_ShouldReturnWindowsFallbackCandidatesInOrder_WhenPlatformIsWindows()
    {
        var root = Path.Combine("C:", "Program Files (x86)", "Steam");

        var candidates = SteamNativeLibraryCandidates.Build(root, OSPlatform.Windows);

        candidates.Should().ContainInOrder(
            Path.Combine(root, "steamclient.dll"),
            Path.Combine(root, "steam_api64.dll"),
            Path.Combine(root, "steam_api.dll"));
    }

    [Fact]
    public void Build_ShouldReturnLinuxCandidatesIncludingRuntimeSubdirectories_WhenPlatformIsLinux()
    {
        const string root = "/home/user/.steam/steam";

        var candidates = SteamNativeLibraryCandidates.Build(root, OSPlatform.Linux);

        candidates.Should().Contain(Path.Combine(root, "libsteam_api.so"));
        candidates.Should().Contain(Path.Combine(root, "steamclient.so"));
        candidates.Should().Contain(Path.Combine(root, "linux64", "libsteam_api.so"));
        candidates.Should().Contain(Path.Combine(root, "ubuntu12_64", "steamclient.so"));
        candidates.Should().Contain(Path.Combine(root, "steamapps", "common", "SteamLinuxRuntime_sniper", "libsteam_api.so"));
        candidates.Should().Contain(Path.Combine(root, "steamapps", "common", "SteamLinuxRuntime_soldier", "steamclient.so"));
    }

    [Fact]
    public void Build_ShouldFallbackToWindowsCandidates_WhenPlatformIsNotLinux()
    {
        var root = Path.Combine("D:", "Steam");

        var candidates = SteamNativeLibraryCandidates.Build(root, OSPlatform.OSX);

        candidates.Should().ContainSingle(path => path.EndsWith("steamclient.dll", StringComparison.OrdinalIgnoreCase));
        candidates.Should().NotContain(path => path.EndsWith(".so", StringComparison.OrdinalIgnoreCase));
    }
}
