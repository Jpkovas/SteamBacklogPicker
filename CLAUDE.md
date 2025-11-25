# CLAUDE.md - AI Assistant Guide for SteamBacklogPicker

This document provides comprehensive guidance for AI assistants working with the SteamBacklogPicker codebase. It covers architecture, patterns, conventions, and workflows to ensure consistent, high-quality contributions.

## Table of Contents

- [Project Overview](#project-overview)
- [Codebase Architecture](#codebase-architecture)
- [Layer Responsibilities](#layer-responsibilities)
- [Key Design Patterns](#key-design-patterns)
- [Development Workflows](#development-workflows)
- [Testing Guidelines](#testing-guidelines)
- [Coding Conventions](#coding-conventions)
- [Common Tasks](#common-tasks)
- [Important Files Reference](#important-files-reference)
- [Do's and Don'ts](#dos-and-donts)

---

## Project Overview

**SteamBacklogPicker** is a .NET 8 WPF desktop application that helps users pick games from their Steam and Epic Games libraries without relying on cloud services. The application operates entirely offline by reading local manifest files and launcher caches.

### Technology Stack

- **Framework**: .NET 8 (Windows Desktop)
- **UI Framework**: WPF (Windows Presentation Foundation)
- **Architecture**: Clean layered architecture with MVVM pattern
- **Testing**: xUnit + FluentAssertions
- **Dependency Injection**: Microsoft.Extensions.DependencyInjection
- **Target Platform**: Windows 10 21H2+ / Windows 11 (64-bit)

### Key Capabilities

- Unified Steam + Epic library discovery (offline-first)
- Collection-aware filtering (Steam collections, tags, install status, storefront)
- Direct manifest/cache parsing (no authentication required)
- Structured telemetry and diagnostics (opt-in)
- Auto-updates via Squirrel

---

## Codebase Architecture

The codebase follows a **clean layered architecture** with clear separation of concerns:

```
SteamBacklogPicker/
├── src/
│   ├── Domain/                          # Core business logic (no external dependencies)
│   ├── Infrastructure/                  # External service integrations
│   │   ├── SteamDiscovery/             # Steam manifest parsing
│   │   ├── EpicDiscovery/              # Epic launcher cache parsing
│   │   └── Telemetry/                  # Logging and telemetry
│   ├── Integration/                     # Platform-specific adapters
│   │   ├── SteamClientAdapter/         # Steamworks.NET wrapper
│   │   ├── ValveFormatParser/          # VDF/ACF file parsing
│   │   └── SteamHooks/                 # File system monitoring
│   └── Presentation/                    # WPF UI layer
│       └── SteamBacklogPicker.UI/      # MVVM ViewModels, Views, Services
├── tests/                               # Test projects mirroring src/ structure
│   ├── Domain/
│   ├── Infrastructure/
│   ├── Integration/
│   ├── Presentation/
│   └── TestUtilities/                  # Shared test fixtures and helpers
├── build/                               # Packaging scripts (MSIX, Squirrel, WinGet)
├── scripts/                             # Build and validation scripts
└── docs/                                # Architecture, requirements, checklists
```

### Dependency Flow

```
Presentation → Integration → Infrastructure → Domain
                    ↓              ↓
                 Domain ← ← ← ← ← ←
```

**Rules:**
- Domain has ZERO external dependencies (pure .NET only)
- Infrastructure and Integration depend on Domain
- Presentation depends on all layers
- No circular dependencies between layers

---

## Layer Responsibilities

### Domain (`src/Domain/`)

**Purpose**: Pure business logic and domain models

**Contains:**
- Domain entities: `GameEntry`, `GameIdentifier`, `SelectionPreferences`
- Domain enums: `OwnershipType`, `InstallState`, `ProductCategory`, `SteamDeckCompatibility`
- Core interfaces: `ISelectionEngine`
- Selection logic and filtering rules

**Key Files:**
- `GameEntry.cs` - Immutable record representing a game
- `GameIdentifier.cs` - Sealed record with storefront-aware identity
- `SelectionEngine.cs` - Game picker with filtering and history tracking

**Conventions:**
- Use `sealed record class` for all domain models
- All properties are `init`-only for immutability
- Provide sensible defaults for all properties
- No I/O, no external dependencies

**Example:**
```csharp
public sealed record class GameEntry
{
    public GameIdentifier Id { get; init; } = GameIdentifier.Unknown;
    public string Title { get; init; } = string.Empty;
    public OwnershipType OwnershipType { get; init; } = OwnershipType.Unknown;
    public InstallState InstallState { get; init; } = InstallState.Unknown;
}
```

---

### Infrastructure (`src/Infrastructure/`)

**Purpose**: External system integrations (file parsing, caching, telemetry)

**Modules:**

#### SteamDiscovery
- **Purpose**: Parse Steam manifests and locate libraries
- **Key Services**: `SteamLibraryLocator`, `SteamAppManifestCache`, `SteamLibraryProvider`
- **File Formats**: `.vdf` (library folders), `.acf` (app manifests)
- **Thread Safety**: Lock-protected caching with `_syncRoot` pattern

#### EpicDiscovery
- **Purpose**: Parse Epic launcher manifests and caches
- **Key Services**: `EpicManifestCache`, `EpicCatalogCache`, `EpicLibraryProvider`
- **File Formats**: `.item` (manifests), `.json` (catalog), `.sqlite` (catalog cache)
- **Paths**: `%PROGRAMDATA%\Epic\EpicGamesLauncher`, `%LOCALAPPDATA%\EpicGamesLauncher`
- **Registration**: `services.AddEpicDiscovery()` extension method

#### Telemetry
- **Purpose**: Opt-in logging and diagnostics
- **Key Services**: `ITelemetryClient`, `ITelemetryConsentStore`
- **Framework**: Serilog with file sink
- **Log Location**: `%LOCALAPPDATA%\SteamBacklogPicker\logs`

**Conventions:**
- Each module provides a `ServiceCollectionExtensions.cs` with `AddXxx()` methods
- Services are registered as Singleton by default
- Thread-safe implementations using locks
- All file I/O abstracted through interfaces for testability

---

### Integration (`src/Integration/`)

**Purpose**: Platform-specific adapters and native interop

**Modules:**

#### SteamClientAdapter
- **Purpose**: Wraps Steamworks.NET and Steam API DLL
- **Key Interface**: `ISteamClientAdapter`
- **Native Dependency**: `steam_api64.dll`
- **Lifecycle**: Implements `IDisposable` for cleanup

#### ValveFormatParser
- **Purpose**: Parse Valve's VDF (text and binary) formats
- **Key Services**: `ValveTextVdfParser`, `ValveBinaryVdfParser`
- **Used By**: SteamDiscovery for parsing library config and manifests

#### SteamHooks
- **Purpose**: File system monitoring for Steam library changes
- **Pattern**: FileSystemWatcher-based auto-refresh

**Conventions:**
- Always implement `IDisposable` for native resources
- Use `ArgumentNullException.ThrowIfNull()` for parameter validation
- Abstract file system access through `IFileAccessor` for testing
- Document native interop with XML comments

---

### Presentation (`src/Presentation/SteamBacklogPicker.UI/`)

**Purpose**: WPF UI with MVVM pattern

**Structure:**
```
SteamBacklogPicker.UI/
├── ViewModels/              # MVVM ViewModels
│   ├── ObservableObject.cs  # Base class with INotifyPropertyChanged
│   ├── RelayCommand.cs      # ICommand for sync operations
│   ├── AsyncRelayCommand.cs # ICommand for async operations
│   ├── MainViewModel.cs     # Root ViewModel
│   └── ...
├── Services/                # UI-layer services (namespaced by feature)
│   ├── GameArt/             # Art locators and downloaders
│   ├── Library/             # Library aggregation
│   ├── Launch/              # Game launching
│   ├── Localization/        # I18N services
│   └── Notifications/       # Toast notifications
├── Views/                   # XAML Views
├── Resources/               # Themes, styles, images
├── App.xaml.cs              # DI container setup and lifecycle
└── appsettings.json         # Configuration overrides
```

**Key Patterns:**

#### ViewModel Base Classes
```csharp
// ObservableObject.cs - Property change notification
public abstract class ObservableObject : INotifyPropertyChanged
{
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

// RelayCommand.cs - Synchronous commands
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;
}

// AsyncRelayCommand.cs - Asynchronous commands with execution tracking
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _executeAsync;
    public bool IsExecuting { get; private set; }
}
```

#### ViewModel Example
```csharp
public sealed class MainViewModel : ObservableObject
{
    private readonly ISelectionEngine _selectionEngine;
    private readonly IGameLibraryService _libraryService;
    private GameDetailsViewModel? _selectedGame;

    public MainViewModel(ISelectionEngine selectionEngine, IGameLibraryService libraryService)
    {
        _selectionEngine = selectionEngine;
        _libraryService = libraryService;
        DrawCommand = new AsyncRelayCommand(DrawGameAsync);
    }

    public GameDetailsViewModel? SelectedGame
    {
        get => _selectedGame;
        private set => SetProperty(ref _selectedGame, value);
    }

    public AsyncRelayCommand DrawCommand { get; }

    private async Task DrawGameAsync(object? parameter)
    {
        var games = await _libraryService.GetLibraryAsync();
        var selected = _selectionEngine.PickNext(games);
        SelectedGame = new GameDetailsViewModel(selected);
    }
}
```

---

## Key Design Patterns

### 1. Dependency Injection

**All services are registered in `App.xaml.cs`:**

```csharp
private static ServiceProvider BuildServices()
{
    var services = new ServiceCollection();

    // Telemetry (with configuration)
    services.AddTelemetryInfrastructure(options =>
    {
        options.ApplicationName = "SteamBacklogPicker";
        options.MinimumLogLevel = LogEventLevel.Information;
    });

    // Infrastructure modules (via extension methods)
    services.AddEpicDiscovery();

    // Steam services (manual registration)
    services.AddSingleton<ISteamLibraryLocator, SteamLibraryLocator>();
    services.AddSingleton<ISteamClientAdapter>(sp => { /* factory */ });

    // Domain services
    services.AddSingleton<ISelectionEngine>(_ => new SelectionEngine());

    // Composite pattern for library providers
    services.AddSingleton<IGameLibraryProvider, SteamLibraryProvider>();
    services.AddSingleton<IGameLibraryProvider, EpicLibraryProvider>();
    services.AddSingleton<IGameLibraryService, CombinedGameLibraryService>();

    // ViewModels
    services.AddSingleton<MainViewModel>();
    services.AddTransient<MainWindow>();

    return services.BuildServiceProvider();
}
```

**Lifetime Conventions:**
- `Singleton` - Long-lived services (caches, parsers, selection engine, root ViewModels)
- `Transient` - UI windows created fresh per request
- Factory lambdas - Complex initialization requiring other services

**Module Registration Pattern:**

Each Infrastructure module provides an extension method:

```csharp
// EpicDiscovery/ServiceCollectionExtensions.cs
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEpicDiscovery(
        this IServiceCollection services,
        Action<EpicLauncherLocatorOptions>? configure = null)
    {
        services.AddOptions<EpicLauncherLocatorOptions>();
        if (configure is not null)
            services.Configure(configure);

        services.AddSingleton<IEpicLauncherLocator, EpicLauncherLocator>();
        services.AddSingleton<EpicManifestCache>();
        services.AddSingleton<EpicCatalogCache>();
        services.AddSingleton<IGameLibraryProvider, EpicLibraryProvider>();

        return services;
    }
}
```

### 2. Composite Pattern

**Multiple implementations composed via DI:**

```csharp
// Multiple library providers injected as IEnumerable
public sealed class CombinedGameLibraryService : IGameLibraryService
{
    private readonly IGameLibraryProvider[] _providers;

    public CombinedGameLibraryService(IEnumerable<IGameLibraryProvider> providers)
    {
        _providers = providers.ToArray(); // DI injects both Steam and Epic providers
    }

    public async Task<IReadOnlyList<GameEntry>> GetLibraryAsync(...)
    {
        var allGames = new List<GameEntry>();
        foreach (var provider in _providers)
        {
            var games = await provider.GetGamesAsync(...);
            allGames.AddRange(games);
        }
        return MergeAndSort(allGames);
    }
}
```

Used for:
- Library providers (Steam + Epic)
- Game art locators (Steam art → Epic art fallback)

### 3. Interface Segregation

**Prefer many small, focused interfaces over large god-interfaces:**

```csharp
public interface ISteamLibraryLocator
{
    IReadOnlyList<string> GetLibraryFolders();
    void Refresh();
}

public interface ISteamRegistryReader
{
    string? GetSteamInstallPath();
}

public interface ISteamLibraryFoldersParser
{
    IReadOnlyList<string> Parse(string vdfContent);
}
```

### 4. Thread-Safe Caching

**Pattern for thread-safe lazy initialization:**

```csharp
public sealed class SteamLibraryLocator : ISteamLibraryLocator, IDisposable
{
    private readonly object _syncRoot = new();
    private IReadOnlyList<string> _cachedLibraries = Array.Empty<string>();
    private bool _initialized;

    public IReadOnlyList<string> GetLibraryFolders()
    {
        EnsureInitialized();
        lock (_syncRoot)
        {
            return _cachedLibraries; // Return cached copy
        }
    }

    public void Refresh()
    {
        lock (_syncRoot)
        {
            UpdateCacheNoLock(); // "_NoLock" suffix indicates lock is already held
        }
    }

    private void UpdateCacheNoLock()
    {
        // Perform update assuming lock is held by caller
        _cachedLibraries = DiscoverLibrariesNoLock();
    }
}
```

**Convention**: Methods ending in `_NoLock` assume the caller holds the lock.

### 5. File System Abstraction

**All file I/O goes through `IFileAccessor` for testability:**

```csharp
public interface IFileAccessor
{
    bool FileExists(string path);
    string ReadAllText(string path);
    Stream OpenRead(string path);
    void CreateDirectory(string path);
    void WriteAllBytes(string path, byte[] contents);
}

public sealed class DefaultFileAccessor : IFileAccessor
{
    public bool FileExists(string path) => File.Exists(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
    // Simple delegation to System.IO
}
```

Tests can provide in-memory implementations without touching disk.

---

## Development Workflows

### Building and Running

```bash
# Restore all NuGet packages
dotnet restore SteamBacklogPicker.sln

# Build in Release configuration
dotnet build SteamBacklogPicker.sln -c Release --no-restore

# Run all tests
dotnet test --no-build --verbosity normal

# Run the UI application
dotnet run --project src/Presentation/SteamBacklogPicker.UI/SteamBacklogPicker.UI.csproj

# Reproduce CI pipeline locally (Git Bash)
scripts/local-pipeline.sh
```

### CI/CD Pipeline

**GitHub Actions** (`.github/workflows/dotnet.yml`):
- Triggers: Push/PR to `main` branch
- Runner: `windows-latest`
- Steps:
  1. Checkout code
  2. Setup .NET 8 SDK
  3. `dotnet restore`
  4. `dotnet build --no-restore`
  5. `dotnet test --no-build --verbosity normal`

### Packaging

```powershell
# MSIX packaging (requires PFX certificate)
pwsh build/package-msix.ps1 -CertificatePath <pfx> `
    -CertificatePassword $env:SBP_CERTIFICATE_PASSWORD `
    -TimestampUrl $env:SBP_TIMESTAMP_URL

# Squirrel installer (auto-updates)
# See build/ directory for scripts
```

### Git Workflow

**Branch Strategy:**
- `main` - Production-ready code
- Feature branches follow naming: `claude/claude-md-<session-id>-<hash>`

**Commit Guidelines:**
- Short, imperative commits (e.g., "Fix Epic catalog parsing for nested JSON")
- Avoid ticket prefixes unless linking in commit body
- Reference issues in PR description, not commits
- Attach screenshots when touching UI

**Before Committing:**
1. Ensure all tests pass (`dotnet test`)
2. Verify CI will succeed (`scripts/local-pipeline.sh`)
3. Check for unintended changes (`git status`, `git diff`)

---

## Testing Guidelines

### Test Organization

Tests mirror the `src/` structure under `tests/`:

```
tests/
├── Domain/Domain.Tests/
├── Infrastructure/
│   ├── SteamDiscovery.Tests/
│   ├── EpicDiscovery.Tests/
│   └── Telemetry.Tests/
├── Integration/
│   ├── SteamClientAdapter.Tests/
│   ├── ValveFormatParser.Tests/
│   └── SteamHooks.Tests/
├── Presentation/SteamBacklogPicker.UI.Tests/
└── TestUtilities/SteamTestUtilities/
```

### Test Naming Convention

**Pattern**: `MethodName_Scenario_ExpectedOutcome`

**Examples:**
```csharp
[Fact]
public void Parse_WhenVdfContainsLibraryFolders_ReturnsAllPaths() { }

[Fact]
public void LaunchGame_WhenGameNotInstalled_SetsErrorMessage() { }

[Fact]
public void GetLibraryFolders_AfterRefresh_ReturnsUpdatedPaths() { }
```

### Test Double Pattern (Fakes over Mocks)

**Do NOT use mocking libraries.** Instead, implement simple fake classes:

```csharp
public sealed class MainViewModelTests
{
    // Inline fake implementation
    private sealed class FakeSelectionEngine : ISelectionEngine
    {
        private SelectionPreferences _preferences = new();

        public SelectionPreferences GetPreferences() => _preferences.Clone();

        public void UpdatePreferences(SelectionPreferences preferences)
        {
            _preferences = preferences;
        }

        public GameEntry PickNext(IEnumerable<GameEntry> games)
        {
            return games.First(); // Simple deterministic behavior
        }
    }

    [Fact]
    public void DrawCommand_WhenLibraryIsEmpty_SetsErrorMessage()
    {
        // Arrange
        var emptyLibrary = Array.Empty<GameEntry>();
        var fakeLibraryService = new FakeGameLibraryService(emptyLibrary);
        var viewModel = new MainViewModel(new FakeSelectionEngine(), fakeLibraryService);

        // Act
        viewModel.DrawCommand.Execute(null);

        // Assert
        viewModel.StatusMessage.Should().Contain("No games found");
    }
}
```

**Benefits:**
- No external mock library dependencies
- Explicit, readable test setup
- Easy to customize behavior per test
- Tests focus on behavior, not implementation

### Test Fixtures

**Location**: `tests/TestUtilities/SteamTestUtilities/Fixtures/`

Contains real Steam manifest files for integration tests:
- `libraryfolders.vdf` - Steam library configuration (VDF format)
- `appmanifest_480.acf` - Game manifest for Portal 2 (ACF format)
- Steam directory structure for realistic parsing

**Usage:**
```csharp
[Fact]
public void ParseManifest_WithRealSteamFile_ExtractsAppDetails()
{
    var fixturePath = Path.Combine(TestContext.FixturesPath, "appmanifest_480.acf");
    var content = File.ReadAllText(fixturePath);

    var result = _parser.Parse(content);

    result.AppId.Should().Be(480);
    result.Name.Should().Be("Spacewar");
}
```

### FluentAssertions Style

**Always use FluentAssertions for readable assertions:**

```csharp
// Good
result.Should().NotBeNull();
result.Title.Should().Be("Portal 2");
games.Should().HaveCount(5);
games.Should().ContainSingle(g => g.Id.AppId == "480");

// Avoid (classic xUnit assertions)
Assert.NotNull(result);
Assert.Equal("Portal 2", result.Title);
Assert.Equal(5, games.Count);
```

### Coverage Requirements

- **Domain logic**: 100% coverage expected
- **Infrastructure**: Focus on parsing logic and caching behavior
- **Integration**: Cover native interop edge cases (initialization failures, disposal)
- **UI ViewModels**: Cover command execution and property changes

---

## Coding Conventions

### General .NET Style

**From `AGENTS.md` and codebase analysis:**

```csharp
// Target .NET 8 with nullable enabled and implicit usings
<TargetFramework>net8.0</TargetFramework>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>

// File-scoped namespaces (preferred)
namespace Domain;

public sealed record class GameEntry
{
    // 4-space indentation
    public string Title { get; init; } = string.Empty;
}
```

### Naming Conventions

| Type | Convention | Example |
|------|-----------|---------|
| **Classes** | PascalCase | `SteamLibraryLocator` |
| **Interfaces** | IPascalCase | `IGameLibraryService` |
| **Records** | PascalCase | `GameEntry`, `GameIdentifier` |
| **Enums** | PascalCase | `OwnershipType`, `InstallState` |
| **Public methods** | PascalCase | `GetLibraryFolders()` |
| **Private methods** | PascalCase | `UpdateCacheNoLock()` |
| **Public properties** | PascalCase | `SelectedGame` |
| **Private fields** | _camelCase | `_selectionEngine`, `_syncRoot` |
| **Parameters** | camelCase | `libraryService`, `preferences` |
| **Local variables** | camelCase | `games`, `selectedEntry` |

### Class and Member Modifiers

```csharp
// Prefer sealed classes (unless designed for inheritance)
public sealed class SteamLibraryLocator : ISteamLibraryLocator

// Records are sealed by default
public sealed record class GameEntry

// Expression-bodied members (use sparingly for clarity)
public string DisplayName => $"{Title} ({Id.AppId})";

// Property initializers with defaults
public string Title { get; init; } = string.Empty;
public InstallState InstallState { get; init; } = InstallState.Unknown;
```

### Immutability Patterns

```csharp
// Domain models: sealed record class with init-only properties
public sealed record class SelectionPreferences
{
    public bool OnlyInstalled { get; init; }
    public IReadOnlySet<string> ExcludedTags { get; init; } = new HashSet<string>();

    // Factory method for cloning with changes
    public SelectionPreferences With(bool? onlyInstalled = null)
    {
        return this with { OnlyInstalled = onlyInstalled ?? OnlyInstalled };
    }
}

// Service state: private readonly fields, public init-only properties
public sealed class CombinedGameLibraryService : IGameLibraryService
{
    private readonly IGameLibraryProvider[] _providers;

    public CombinedGameLibraryService(IEnumerable<IGameLibraryProvider> providers)
    {
        _providers = providers.ToArray(); // Defensive copy
    }
}
```

### Null Safety

```csharp
// Parameter validation
public SteamLibraryLocator(ISteamRegistryReader registryReader)
{
    ArgumentNullException.ThrowIfNull(registryReader);
    _registryReader = registryReader;
}

// Nullable reference types enabled - use ? for nullable
public string? GetSteamInstallPath() => /* may return null */;

// Pattern matching for safe optional access
if (_serviceProvider.GetService<ITelemetryClient>() is { } telemetryClient)
{
    telemetryClient.TrackEvent("event_name");
}
```

### Dispose Pattern

```csharp
public sealed class SteamLibraryLocator : ISteamLibraryLocator, IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;

        // Release resources
        _fileWatcher?.Dispose();

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SteamLibraryLocator));
    }
}
```

---

## Common Tasks

### Adding a New Domain Model

1. Create a `sealed record class` in `src/Domain/`
2. Use `init`-only properties with sensible defaults
3. Add XML documentation comments
4. Add unit tests in `tests/Domain/Domain.Tests/`

**Example:**
```csharp
namespace Domain;

/// <summary>
/// Represents a user's game ownership and installation details.
/// </summary>
public sealed record class GameEntry
{
    /// <summary>
    /// Unique identifier for the game, including storefront context.
    /// </summary>
    public GameIdentifier Id { get; init; } = GameIdentifier.Unknown;

    /// <summary>
    /// Display name of the game as shown in the library.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    // ... more properties
}
```

### Adding a New Infrastructure Service

1. Create interface in appropriate Infrastructure module
2. Implement as `sealed class` with constructor DI
3. Add to `ServiceCollectionExtensions.cs` for the module
4. Register in `App.xaml.cs` via extension method
5. Add unit tests with fake implementations

**Example:**
```csharp
// 1. Define interface
public interface IGameMetadataCache
{
    Task<GameMetadata?> GetMetadataAsync(string appId);
}

// 2. Implement service
public sealed class GameMetadataCache : IGameMetadataCache
{
    private readonly IFileAccessor _fileAccessor;
    private readonly ILogger<GameMetadataCache> _logger;

    public GameMetadataCache(IFileAccessor fileAccessor, ILogger<GameMetadataCache> logger)
    {
        ArgumentNullException.ThrowIfNull(fileAccessor);
        ArgumentNullException.ThrowIfNull(logger);
        _fileAccessor = fileAccessor;
        _logger = logger;
    }

    public async Task<GameMetadata?> GetMetadataAsync(string appId)
    {
        // Implementation
    }
}

// 3. Register in ServiceCollectionExtensions.cs
public static IServiceCollection AddSteamDiscovery(this IServiceCollection services)
{
    services.AddSingleton<IGameMetadataCache, GameMetadataCache>();
    return services;
}

// 4. Use in App.xaml.cs (already registered via extension method)
services.AddSteamDiscovery();
```

### Adding a New ViewModel

1. Inherit from `ObservableObject`
2. Use `SetProperty<T>()` for property setters
3. Define commands as `RelayCommand` or `AsyncRelayCommand`
4. Inject dependencies via constructor
5. Register in `App.xaml.cs`

**Example:**
```csharp
public sealed class SettingsViewModel : ObservableObject
{
    private readonly ITelemetryConsentStore _consentStore;
    private bool _telemetryEnabled;

    public SettingsViewModel(ITelemetryConsentStore consentStore)
    {
        ArgumentNullException.ThrowIfNull(consentStore);
        _consentStore = consentStore;

        SaveCommand = new AsyncRelayCommand(SaveSettingsAsync);
        LoadSettings();
    }

    public bool TelemetryEnabled
    {
        get => _telemetryEnabled;
        set => SetProperty(ref _telemetryEnabled, value);
    }

    public AsyncRelayCommand SaveCommand { get; }

    private void LoadSettings()
    {
        TelemetryEnabled = _consentStore.GetConsent();
    }

    private async Task SaveSettingsAsync(object? parameter)
    {
        await _consentStore.SetConsentAsync(TelemetryEnabled);
        // Notify user of save success
    }
}
```

### Adding Tests for a New Feature

1. Create test class in matching `tests/` directory
2. Use inline `Fake` implementations for dependencies
3. Follow `MethodName_Scenario_ExpectedOutcome` naming
4. Use FluentAssertions for readable assertions
5. Arrange-Act-Assert structure

**Example:**
```csharp
public sealed class GameMetadataCacheTests
{
    private sealed class FakeFileAccessor : IFileAccessor
    {
        private readonly Dictionary<string, string> _files = new();

        public void AddFile(string path, string content) => _files[path] = content;
        public bool FileExists(string path) => _files.ContainsKey(path);
        public string ReadAllText(string path) => _files[path];
        // ... other IFileAccessor members
    }

    [Fact]
    public async Task GetMetadataAsync_WhenFileExists_ReturnsMetadata()
    {
        // Arrange
        var fakeFileAccessor = new FakeFileAccessor();
        fakeFileAccessor.AddFile(@"C:\cache\480.json", @"{""title"":""Portal 2""}");
        var cache = new GameMetadataCache(fakeFileAccessor, NullLogger<GameMetadataCache>.Instance);

        // Act
        var result = await cache.GetMetadataAsync("480");

        // Assert
        result.Should().NotBeNull();
        result!.Title.Should().Be("Portal 2");
    }

    [Fact]
    public async Task GetMetadataAsync_WhenFileNotFound_ReturnsNull()
    {
        // Arrange
        var fakeFileAccessor = new FakeFileAccessor();
        var cache = new GameMetadataCache(fakeFileAccessor, NullLogger<GameMetadataCache>.Instance);

        // Act
        var result = await cache.GetMetadataAsync("999");

        // Assert
        result.Should().BeNull();
    }
}
```

---

## Important Files Reference

### Configuration and Build

| File | Purpose |
|------|---------|
| `SteamBacklogPicker.sln` | Main solution file (all projects) |
| `Directory.Build.props` | Repository-wide MSBuild properties |
| `Directory.Build.targets` | Windows platform targeting configuration |
| `src/Presentation/SteamBacklogPicker.UI/appsettings.json` | Application configuration (Epic paths, logging) |
| `.github/workflows/dotnet.yml` | GitHub Actions CI pipeline |
| `scripts/local-pipeline.sh` | Local CI simulation script |

### Documentation

| File | Purpose |
|------|---------|
| `README.md` | User-facing project overview and installation |
| `AGENTS.md` | Repository guidelines for developers and AI agents |
| `CLAUDE.md` | This file - comprehensive AI assistant guide |
| `CHANGES.md` | Recent changes and release notes |
| `TODO.md` | Outstanding work items |
| `docs/architecture.md` | Detailed architecture documentation (Portuguese) |
| `docs/requirements.md` | Functional and non-functional requirements |

### Entry Points

| File | Purpose |
|------|---------|
| `src/Presentation/SteamBacklogPicker.UI/App.xaml.cs` | Application entry point, DI setup, lifecycle |
| `src/Presentation/SteamBacklogPicker.UI/MainWindow.xaml` | Main application window (WPF) |
| `src/Domain/SelectionEngine.cs` | Core game selection logic |

### Service Registration

| File | Purpose |
|------|---------|
| `src/Infrastructure/EpicDiscovery/ServiceCollectionExtensions.cs` | Epic services DI registration |
| `src/Infrastructure/Telemetry/ServiceCollectionExtensions.cs` | Telemetry services DI registration |
| `src/Presentation/SteamBacklogPicker.UI/App.xaml.cs` | Main DI container configuration |

### Test Utilities

| Path | Purpose |
|------|---------|
| `tests/TestUtilities/SteamTestUtilities/Fixtures/` | Real Steam manifest files for testing |
| `tests/Presentation/SteamBacklogPicker.UI.Tests/MainViewModelTests.cs` | Example of fake pattern usage |

---

## Do's and Don'ts

### Architecture and Design

#### DO:
- ✅ Respect layer boundaries (Domain has no external dependencies)
- ✅ Use DI for all service instantiation
- ✅ Implement interfaces for all services
- ✅ Use `sealed` for all classes unless inheritance is required
- ✅ Use `record class` for immutable DTOs
- ✅ Provide extension methods for module registration (`AddXxx()`)
- ✅ Abstract file I/O through `IFileAccessor` or similar

#### DON'T:
- ❌ Add external dependencies to Domain layer
- ❌ Instantiate services with `new` (except in DI setup)
- ❌ Create circular dependencies between layers
- ❌ Make classes inheritable without good reason
- ❌ Use mutable DTOs in domain models
- ❌ Perform direct file I/O in testable code

### Code Style

#### DO:
- ✅ Enable nullable reference types
- ✅ Use file-scoped namespaces
- ✅ Use 4-space indentation
- ✅ Validate parameters with `ArgumentNullException.ThrowIfNull()`
- ✅ Document public APIs with XML comments
- ✅ Use `var` for obvious types
- ✅ Prefer LINQ for collections
- ✅ Use `SetProperty<T>()` in ViewModels

#### DON'T:
- ❌ Use tabs (use 4 spaces)
- ❌ Skip null checks on constructor parameters
- ❌ Use `#nullable disable` or suppress warnings unnecessarily
- ❌ Create god-classes with multiple responsibilities
- ❌ Use expression bodies when they harm readability
- ❌ Directly access backing fields in ViewModels (use properties)

### Testing

#### DO:
- ✅ Write tests for all new domain logic
- ✅ Use inline fake implementations for test doubles
- ✅ Follow `MethodName_Scenario_ExpectedOutcome` naming
- ✅ Use FluentAssertions for assertions
- ✅ Use Arrange-Act-Assert structure
- ✅ Test edge cases and error conditions
- ✅ Use realistic fixtures for integration tests

#### DON'T:
- ❌ Use mocking libraries (use fakes instead)
- ❌ Test implementation details (test behavior)
- ❌ Share mutable state between tests
- ❌ Write tests that depend on execution order
- ❌ Skip cleanup in `Dispose()` or `finally` blocks
- ❌ Use classic Assert.* when FluentAssertions is available

### Git and Commits

#### DO:
- ✅ Write short, imperative commit messages
- ✅ Run `dotnet test` before committing
- ✅ Check `git status` and `git diff` before committing
- ✅ Reference issues in PR descriptions
- ✅ Attach screenshots when changing UI
- ✅ Ensure CI passes before requesting review

#### DON'T:
- ❌ Commit commented-out code
- ❌ Commit generated files (bin/, obj/, .vs/)
- ❌ Push directly to `main` (use feature branches)
- ❌ Force push to shared branches
- ❌ Skip CI validation

### Thread Safety and Concurrency

#### DO:
- ✅ Use locks for shared mutable state
- ✅ Document `_NoLock` suffix for lock-protected methods
- ✅ Use `async`/`await` for I/O operations
- ✅ Implement `IDisposable` for resource cleanup
- ✅ Use `CancellationToken` for long-running operations

#### DON'T:
- ❌ Access shared state without synchronization
- ❌ Use `Task.Wait()` or `.Result` (causes deadlocks)
- ❌ Forget to dispose native resources
- ❌ Hold locks across `await` boundaries (use `SemaphoreSlim`)

### Error Handling

#### DO:
- ✅ Validate inputs at public API boundaries
- ✅ Log errors with context (use structured logging)
- ✅ Provide sensible defaults for degraded functionality
- ✅ Use specific exception types
- ✅ Include user-facing error messages in ViewModels

#### DON'T:
- ❌ Swallow exceptions silently
- ❌ Use exceptions for flow control
- ❌ Expose internal exception details to users
- ❌ Catch overly broad exception types (catch `Exception`)

---

## Quick Reference Checklist

When implementing a new feature, ensure:

- [ ] Layer boundaries respected (Domain isolated)
- [ ] Services registered in DI container
- [ ] Interfaces defined for all services
- [ ] Classes are `sealed` unless inheritance required
- [ ] Domain models are `sealed record class`
- [ ] All properties have sensible defaults
- [ ] Nullable reference types used correctly
- [ ] Parameters validated with `ArgumentNullException.ThrowIfNull()`
- [ ] File I/O abstracted through interfaces
- [ ] Thread-safe implementation for shared state
- [ ] `IDisposable` implemented for resources
- [ ] XML documentation on public APIs
- [ ] Unit tests written with fakes
- [ ] Tests follow naming convention
- [ ] FluentAssertions used for assertions
- [ ] CI pipeline passes (`dotnet test`)
- [ ] Commit message is short and imperative

---

## Additional Resources

- **AGENTS.md** - Quick developer reference for structure, build, and commit guidelines
- **docs/architecture.md** - Detailed architecture diagrams and component descriptions
- **docs/requirements.md** - Functional and non-functional requirements
- **README.md** - User-facing installation and usage guide
- **CHANGES.md** - Recent changes and release history

---

## Version Information

- **Document Version**: 1.0
- **Codebase Version**: As of commit `e73b9e5`
- **Last Updated**: 2025-11-25
- **Maintained By**: AI assistants working with this repository

---

**Note to AI Assistants**: This document is comprehensive but not exhaustive. When in doubt:
1. Check existing code for patterns
2. Consult `AGENTS.md` for quick guidelines
3. Review recent commits in `CHANGES.md`
4. Ask the user for clarification on ambiguous requirements
