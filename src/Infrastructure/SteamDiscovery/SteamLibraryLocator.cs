using System.IO;
using System.Linq;

namespace SteamDiscovery;

public sealed class SteamLibraryLocator : IDisposable
{
    private readonly ISteamRegistryReader _registryReader;
    private readonly ISteamLibraryFoldersParser _parser;
    private readonly object _syncRoot = new();
    private FileSystemWatcher? _watcher;
    private string? _libraryFilePath;
    private IReadOnlyList<string> _cachedLibraries = Array.Empty<string>();
    private bool _initialized;

    public SteamLibraryLocator(ISteamRegistryReader registryReader, ISteamLibraryFoldersParser parser)
    {
        _registryReader = registryReader ?? throw new ArgumentNullException(nameof(registryReader));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    public IReadOnlyList<string> GetLibraryFolders()
    {
        EnsureInitialized();
        lock (_syncRoot)
        {
            return _cachedLibraries;
        }
    }

    public void Refresh()
    {
        lock (_syncRoot)
        {
            UpdateCacheNoLock();
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_initialized)
            {
                return;
            }

            InitializeWatcherNoLock();
            _initialized = true;
        }
    }

    private void InitializeWatcherNoLock()
    {
        var steamPath = _registryReader.GetSteamInstallPath();
        if (string.IsNullOrWhiteSpace(steamPath))
        {
            _libraryFilePath = null;
            _cachedLibraries = Array.Empty<string>();
            return;
        }

        var libraryFilePath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        _libraryFilePath = libraryFilePath;
        UpdateCacheNoLock();

        var directory = Path.GetDirectoryName(libraryFilePath);
        var fileName = Path.GetFileName(libraryFilePath);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
        {
            return;
        }

        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            var watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = false
            };

            watcher.Changed += OnLibraryFileChanged;
            watcher.Created += OnLibraryFileChanged;
            watcher.Renamed += OnLibraryFileRenamed;
            watcher.Deleted += OnLibraryFileDeleted;
            watcher.EnableRaisingEvents = true;

            _watcher = watcher;
        }
        catch (IOException)
        {
            DisposeWatcherNoLock();
        }
        catch (UnauthorizedAccessException)
        {
            DisposeWatcherNoLock();
        }
        catch (PlatformNotSupportedException)
        {
            DisposeWatcherNoLock();
        }
    }

    private void OnLibraryFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_syncRoot)
        {
            if (_libraryFilePath is null || !FilePathMatches(e.FullPath))
            {
                return;
            }

            UpdateCacheNoLock();
        }
    }

    private void OnLibraryFileRenamed(object sender, RenamedEventArgs e)
    {
        lock (_syncRoot)
        {
            if (_libraryFilePath is null)
            {
                return;
            }

            if (FilePathMatches(e.OldFullPath) || FilePathMatches(e.FullPath))
            {
                _libraryFilePath = e.FullPath;
                UpdateCacheNoLock();
            }
        }
    }

    private void OnLibraryFileDeleted(object sender, FileSystemEventArgs e)
    {
        lock (_syncRoot)
        {
            if (_libraryFilePath is null || !FilePathMatches(e.FullPath))
            {
                return;
            }

            _cachedLibraries = Array.Empty<string>();
        }
    }

    private bool FilePathMatches(string fullPath)
    {
        if (_libraryFilePath is null)
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(_libraryFilePath), Path.GetFullPath(fullPath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void UpdateCacheNoLock()
    {
        var filePath = _libraryFilePath;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            _cachedLibraries = Array.Empty<string>();
            return;
        }

        try
        {
            var content = File.ReadAllText(filePath);
            var parsed = _parser.Parse(content);
            _cachedLibraries = parsed.ToArray();
        }
        catch (IOException)
        {
            // keep previous cache on transient IO errors
        }
        catch (UnauthorizedAccessException)
        {
            // keep previous cache on permission issues
        }
        catch (FormatException)
        {
            _cachedLibraries = Array.Empty<string>();
        }
    }

    private void DisposeWatcherNoLock()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        lock (_syncRoot)
        {
            DisposeWatcherNoLock();
        }
    }
}
