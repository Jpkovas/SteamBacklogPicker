using System.IO;
using System.Linq;

namespace SteamDiscovery;

public interface ISteamLibraryLocator
{
    IReadOnlyList<string> GetLibraryFolders();

    void Refresh();
}

public sealed class SteamLibraryLocator : ISteamLibraryLocator, IDisposable
{
    private readonly ISteamInstallPathProvider _installPathProvider;
    private readonly ISteamLibraryFoldersParser _parser;
    private readonly IPathComparisonStrategy _pathComparison;
    private readonly object _syncRoot = new();
    private FileSystemWatcher? _watcher;
    private string? _libraryFilePath;
    private IReadOnlyList<string> _cachedLibraries = Array.Empty<string>();
    private bool _initialized;

    public SteamLibraryLocator(ISteamInstallPathProvider installPathProvider, ISteamLibraryFoldersParser parser)
        : this(installPathProvider, parser, new PlatformPathComparisonStrategy(new RuntimePlatformProvider()))
    {
    }

    public SteamLibraryLocator(
        ISteamInstallPathProvider installPathProvider,
        ISteamLibraryFoldersParser parser,
        IPathComparisonStrategy pathComparison)
    {
        _installPathProvider = installPathProvider ?? throw new ArgumentNullException(nameof(installPathProvider));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _pathComparison = pathComparison ?? throw new ArgumentNullException(nameof(pathComparison));
    }

    public IReadOnlyList<string> GetLibraryFolders()
    {
        EnsureInitialized();
        lock (_syncRoot)
        {
            EnsureTrackedLibraryFileNoLock();
            return _cachedLibraries;
        }
    }

    public void Refresh()
    {
        lock (_syncRoot)
        {
            if (!_initialized)
            {
                InitializeWatcherNoLock();
                _initialized = true;
                return;
            }
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
        var steamPath = _installPathProvider.GetSteamInstallPath();
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

            var watcher = new FileSystemWatcher(directory, "*")
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

            var oldMatches = FilePathMatches(e.OldFullPath);
            var newMatches = FilePathMatches(e.FullPath);

            if (!oldMatches && !newMatches)
            {
                return;
            }

            if (newMatches)
            {
                _libraryFilePath = e.FullPath;
                UpdateCacheNoLock();
                return;
            }

            if (oldMatches)
            {
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
            return _pathComparison.Equals(Path.GetFullPath(_libraryFilePath), Path.GetFullPath(fullPath));
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
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var parsed = _parser.Parse(stream);
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

    private void EnsureTrackedLibraryFileNoLock()
    {
        var filePath = _libraryFilePath;
        if (string.IsNullOrEmpty(filePath) || File.Exists(filePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            UpdateCacheNoLock();
            return;
        }

        try
        {
            var replacement = Directory
                .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(FilePathMatches);

            if (replacement is not null)
            {
                _libraryFilePath = replacement;
            }

            UpdateCacheNoLock();
        }
        catch (IOException)
        {
            // keep previous cache on transient IO errors
        }
        catch (UnauthorizedAccessException)
        {
            // keep previous cache on permission issues
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
