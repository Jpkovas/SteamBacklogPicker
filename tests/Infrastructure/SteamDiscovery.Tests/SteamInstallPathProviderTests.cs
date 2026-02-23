using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using SteamDiscovery;
using Xunit;

namespace SteamDiscovery.Tests;

public sealed class SteamInstallPathProviderTests
{
    [Fact]
    public void LinuxProvider_ShouldPreferSteamPathEnvironmentVariable()
    {
        var env = new FakeEnvironmentProvider(
            new Dictionary<string, string?> { ["STEAM_PATH"] = "/custom/steam" },
            "/home/test");
        var fs = new FakeFileSystem(
            directories: new[] { "/custom/steam" },
            files: new[] { "/custom/steam/steamapps/libraryfolders.vdf" });

        var sut = new LinuxSteamInstallPathProvider(env, fs);

        var path = sut.GetSteamInstallPath();

        Assert.Equal("/custom/steam", path);
    }

    [Fact]
    public void LinuxProvider_ShouldFollowPriorityAfterEnvironmentVariable()
    {
        var home = "/home/test";
        var env = new FakeEnvironmentProvider(new Dictionary<string, string?>(), home);
        var expected = Path.Combine(home, ".local", "share", "Steam");
        var fs = new FakeFileSystem(
            directories: new[]
            {
                Path.Combine(home, ".steam", "steam"),
                expected
            },
            files: new[] { Path.Combine(expected, "steamapps", "libraryfolders.vdf") });

        var sut = new LinuxSteamInstallPathProvider(env, fs);

        var path = sut.GetSteamInstallPath();

        Assert.Equal(expected, path);
    }

    [Fact]
    public void LinuxProvider_ShouldReturnFlatpakPathWhenOthersAreMissing()
    {
        var home = "/home/test";
        var env = new FakeEnvironmentProvider(new Dictionary<string, string?>(), home);
        var flatpakPath = Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
        var fs = new FakeFileSystem(
            directories: new[] { flatpakPath },
            files: new[] { Path.Combine(flatpakPath, "steamapps", "libraryfolders.vdf") });

        var sut = new LinuxSteamInstallPathProvider(env, fs);

        var path = sut.GetSteamInstallPath();

        Assert.Equal(flatpakPath, path);
    }

    [Fact]
    public void DefaultProvider_ShouldUseLinuxProviderOnLinux()
    {
        var windows = new StubWindowsProvider("windows");
        var linux = new StubLinuxProvider("linux");
        var sut = new DefaultSteamInstallPathProvider(new FakePlatformProvider(isWindows: false, isLinux: true), windows, linux);

        var result = sut.GetSteamInstallPath();

        Assert.Equal("linux", result);
    }

    [Fact]
    public void DefaultProvider_ShouldUseWindowsProviderOnWindows()
    {
        var windows = new StubWindowsProvider("windows");
        var linux = new StubLinuxProvider("linux");
        var sut = new DefaultSteamInstallPathProvider(new FakePlatformProvider(isWindows: true, isLinux: false), windows, linux);

        var result = sut.GetSteamInstallPath();

        Assert.Equal("windows", result);
    }


    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SteamLibraryLocator_FilePathMatches_ShouldRespectPlatformPathComparison(bool isWindows)
    {
        var provider = new FixedInstallPathProvider("/tmp/steam");
        var parser = new SteamLibraryFoldersParser();
        var comparison = new PlatformPathComparisonStrategy(new FakePlatformProvider(isWindows: isWindows, isLinux: !isWindows));
        using var sut = new SteamLibraryLocator(provider, parser, comparison);

        var libraryPathField = typeof(SteamLibraryLocator).GetField("_libraryFilePath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(libraryPathField);
        libraryPathField!.SetValue(sut, "/tmp/Steam/steamapps/libraryfolders.vdf");

        var method = typeof(SteamLibraryLocator).GetMethod("FilePathMatches", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        var matches = (bool)method!.Invoke(sut, new object[] { "/tmp/steam/steamapps/libraryfolders.vdf" })!;

        Assert.Equal(isWindows, matches);
    }

    [Fact]
    public void SteamLibraryLocator_ShouldReadFromInstallPathProvider()
    {
        using var root = new TempDirectory();
        var steamRoot = Path.Combine(root.Path, "Steam");
        var steamApps = Path.Combine(steamRoot, "steamapps");
        Directory.CreateDirectory(steamApps);
        var libraryFile = Path.Combine(steamApps, "libraryfolders.vdf");
        File.WriteAllText(libraryFile, "\"LibraryFolders\" { \"0\" \"C:\\\\Steam\" }");

        var provider = new FixedInstallPathProvider(steamRoot);
        var parser = new SteamLibraryFoldersParser();
        using var sut = new SteamLibraryLocator(provider, parser);

        var result = sut.GetLibraryFolders();

        Assert.Single(result);
        Assert.Equal("C:\\Steam", result.Single());
    }

    [Fact]
    public void SteamLibraryLocator_RenameAwayAndRecreate_ShouldKeepTrackingCanonicalLibraryFile()
    {
        using var root = new TempDirectory();
        var steamRoot = Path.Combine(root.Path, "Steam");
        var steamApps = Path.Combine(steamRoot, "steamapps");
        Directory.CreateDirectory(steamApps);

        var canonicalLibraryFile = Path.Combine(steamApps, "libraryfolders.vdf");
        var renamedLibraryFile = Path.Combine(steamApps, "libraryfolders.vdf.bak");

        File.WriteAllText(canonicalLibraryFile, "\"LibraryFolders\" { \"0\" \"C:\\\\Steam\" }");

        var provider = new FixedInstallPathProvider(steamRoot);
        var parser = new SteamLibraryFoldersParser();
        using var sut = new SteamLibraryLocator(provider, parser);

        var renamedHandler = typeof(SteamLibraryLocator).GetMethod("OnLibraryFileRenamed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var changedHandler = typeof(SteamLibraryLocator).GetMethod("OnLibraryFileChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var libraryPathField = typeof(SteamLibraryLocator).GetField("_libraryFilePath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(renamedHandler);
        Assert.NotNull(changedHandler);
        Assert.NotNull(libraryPathField);

        _ = sut.GetLibraryFolders();

        File.Move(canonicalLibraryFile, renamedLibraryFile);
        var renamedArgs = new RenamedEventArgs(WatcherChangeTypes.Renamed, steamApps, Path.GetFileName(renamedLibraryFile), Path.GetFileName(canonicalLibraryFile));
        renamedHandler!.Invoke(sut, new object[] { sut, renamedArgs });

        File.WriteAllText(canonicalLibraryFile, "\"LibraryFolders\" { \"0\" \"D:\\\\SteamLibrary\" }");
        var changedArgs = new FileSystemEventArgs(WatcherChangeTypes.Changed, steamApps, Path.GetFileName(canonicalLibraryFile));
        changedHandler!.Invoke(sut, new object[] { sut, changedArgs });

        var trackedPath = libraryPathField!.GetValue(sut) as string;
        trackedPath.Should().Be(canonicalLibraryFile);

        var libraries = sut.GetLibraryFolders();
        libraries.Should().ContainSingle().Which.Should().Be("D:\\SteamLibrary");
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

    private sealed class StubWindowsProvider : IWindowsSteamInstallPathProvider
    {
        private readonly string _value;

        public StubWindowsProvider(string value)
        {
            _value = value;
        }

        public string? GetSteamInstallPath() => _value;
    }

    private sealed class StubLinuxProvider : ILinuxSteamInstallPathProvider
    {
        private readonly string _value;

        public StubLinuxProvider(string value)
        {
            _value = value;
        }

        public string? GetSteamInstallPath() => _value;
    }

    private sealed class FakeEnvironmentProvider : IEnvironmentProvider
    {
        private readonly IReadOnlyDictionary<string, string?> _variables;
        private readonly string? _home;

        public FakeEnvironmentProvider(IReadOnlyDictionary<string, string?> variables, string? home)
        {
            _variables = variables;
            _home = home;
        }

        public string? GetEnvironmentVariable(string variable)
            => _variables.TryGetValue(variable, out var value) ? value : null;

        public string? GetHomeDirectory() => _home;
    }

    private sealed class FakeFileSystem : IFileSystem
    {
        private readonly HashSet<string> _directories;
        private readonly HashSet<string> _files;

        public FakeFileSystem(IEnumerable<string> directories, IEnumerable<string> files)
        {
            _directories = new HashSet<string>(directories);
            _files = new HashSet<string>(files);
        }

        public bool DirectoryExists(string path) => _directories.Contains(path);

        public bool FileExists(string path) => _files.Contains(path);
    }

    private sealed class FakePlatformProvider : IPlatformProvider
    {
        private readonly bool _isWindows;
        private readonly bool _isLinux;

        public FakePlatformProvider(bool isWindows, bool isLinux)
        {
            _isWindows = isWindows;
            _isLinux = isLinux;
        }

        public bool IsWindows() => _isWindows;

        public bool IsLinux() => _isLinux;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SteamInstallPathProviderTests", Guid.NewGuid().ToString("N"));
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
