using System;
using System.IO;
using FluentAssertions;
using SteamBacklogPicker.UI.Services.Runtime;
using SteamDiscovery;
using Xunit;

namespace SteamBacklogPicker.UI.Tests;

public sealed class SteamEnvironmentTests : IDisposable
{
    private readonly string _originalSteamPath;

    public SteamEnvironmentTests()
    {
        _originalSteamPath = Environment.GetEnvironmentVariable("STEAM_PATH") ?? string.Empty;
    }

    [Fact]
    public void GetSteamDirectory_ShouldReturnInstallPathProviderDirectory_WhenItExists()
    {
        using var root = new TempDirectory();
        var sut = new SteamEnvironment(new FixedInstallPathProvider(root.Path));

        var result = sut.GetSteamDirectory();

        result.Should().Be(root.Path);
    }

    [Fact]
    public void GetSteamDirectory_ShouldNotFallbackToEnvironmentVariable_WhenInstallPathProviderIsEmpty()
    {
        using var root = new TempDirectory();
        Environment.SetEnvironmentVariable("STEAM_PATH", root.Path);
        var sut = new SteamEnvironment(new FixedInstallPathProvider(null));

        var result = sut.GetSteamDirectory();

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetSteamDirectory_ShouldReturnEmpty_WhenInstallPathProviderDirectoryDoesNotExist()
    {
        using var root = new TempDirectory();
        var missingPath = Path.Combine(root.Path, "missing");
        var sut = new SteamEnvironment(new FixedInstallPathProvider(missingPath));

        var result = sut.GetSteamDirectory();

        result.Should().BeEmpty();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("STEAM_PATH", string.IsNullOrEmpty(_originalSteamPath) ? null : _originalSteamPath);
    }

    private sealed class FixedInstallPathProvider : ISteamInstallPathProvider
    {
        private readonly string? _path;

        public FixedInstallPathProvider(string? path)
        {
            _path = path;
        }

        public string? GetSteamInstallPath() => _path;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SteamEnvironmentTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
