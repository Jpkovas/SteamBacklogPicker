# CLAUDE.md - AI Assistant Guide for SteamBacklogPicker

This guide reflects the current Steam-only .NET 8 codebase. Keep changes small, behavior-preserving, and backed by the relevant build or test command.

## Project Overview

SteamBacklogPicker is an offline-first desktop app that helps users choose a game from their local Steam library. The app reads Steam manifests and local Steam client data; it does not depend on cloud services for normal library discovery.

Current UI targets:

- Windows: WPF project at `src/Presentation/SteamBacklogPicker.UI`.
- Linux: Avalonia project at `src/Presentation/SteamBacklogPicker.Linux`.

Shared application behavior lives in `src/Presentation/SteamBacklogPicker.AppCore`.

## Architecture

```text
src/
  Domain/                         Pure models, preferences, history, selection rules
  Infrastructure/
    SteamDiscovery/               Steam install/library/appmanifest discovery and cache
    Telemetry/                    Local diagnostics and consent-aware telemetry contracts
  Integration/
    SteamClientAdapter/           Steamworks.NET wrapper and local Steam VDF fallback
    SteamHooks/                   File/watch integration helpers
    ValveFormatParser/            Valve text/binary format parsing
  Presentation/
    SteamBacklogPicker.AppCore/   Shared ViewModels and platform-neutral services
    SteamBacklogPicker.UI/        WPF shell and Windows UX services
    SteamBacklogPicker.Linux/     Avalonia shell and Linux UX services
tests/
  ...                             Test projects mirror production layers
```

Dependency direction:

```text
Presentation -> AppCore -> Infrastructure/Integration -> Domain
```

Domain must stay independent of I/O, UI frameworks, Steamworks, and dependency injection.

## Layer Responsibilities

### Domain

- `GameEntry`, `GameIdentifier`, `SelectionPreferences`, selection history, enums, and filtering logic.
- Prefer immutable record classes and explicit defaults.
- Cover rule changes in `tests/Domain/Domain.Tests`.

### SteamDiscovery

- Locates Steam installs and library folders.
- Parses `libraryfolders.vdf` and `appmanifest_*.acf`.
- Uses platform-aware path comparison for Windows vs Linux casing behavior.
- Maintains cache state defensively when files are renamed, rewritten, or temporarily unreadable.

### SteamClientAdapter

- Wraps Steamworks.NET and native Steam API lifecycle.
- Provides local VDF fallback metadata when native calls are unavailable.
- Native resources must be reset and disposed predictably.

### AppCore

- Owns shared ViewModels, localization, game launch options, art lookup contracts, library aggregation, and shared DI registration.
- Platform projects should only add platform UX services such as notifications and update strategy.

### Platform Projects

- WPF owns Windows UI, toast/update implementation, and Windows-specific resources.
- Linux owns Avalonia UI, Linux notifications, Linux update flow, and Linux-specific resources.
- Do not duplicate business rules across UI projects.

## Build and Test Commands

Use the repo-local pipeline for PR confidence:

```bash
dotnet restore SteamBacklogPicker.sln
scripts/local-pipeline.sh
dotnet build SteamBacklogPicker.sln -c Release --no-restore
```

Useful focused commands:

```bash
dotnet test tests/Domain/Domain.Tests/Domain.Tests.csproj -c Release -f net8.0
dotnet test tests/Infrastructure/SteamDiscovery.Tests/SteamDiscovery.Tests.csproj -c Release -f net8.0
dotnet test tests/Presentation/SteamBacklogPicker.Linux.Tests/SteamBacklogPicker.Linux.Tests.csproj -c Release -f net8.0
dotnet build tests/Presentation/SteamBacklogPicker.UI.Tests/SteamBacklogPicker.UI.Tests.csproj -c Release -f net8.0-windows10.0.18362.0
```

On macOS, Windows-targeted projects can compile with Windows targeting enabled, but WPF tests do not execute because the Windows Desktop runtime is unavailable.

## CI and Release Artifacts

- Main CI lives in `.github/workflows/dotnet.yml`.
- Linux release packaging lives in `.github/workflows/linux-release.yml` and `scripts/package-linux-appimage.sh`.
- The current Linux package is a portable self-contained x64 executable plus `linux-appimage-update.json`; it is not a native AppImage yet.
- The current Windows artifact workflow publishes raw `dotnet publish` output. Squirrel/MSIX installer automation is not currently tracked in this repository.

## Coding Conventions

- Target .NET 8 with nullable enabled.
- Use file-scoped namespaces and 4-space indentation.
- Keep public types and members in `PascalCase`; locals and parameters in `camelCase`.
- Prefer straightforward code over terse expression bodies in selection or persistence logic.
- Add comments only when they clarify a non-obvious invariant.

## Testing Conventions

- Tests should follow `MethodName_ShouldExpectation`.
- Keep fixtures under `tests/TestUtilities` when reused across test projects.
- Use temporary directories for file-system tests and clean them in `Dispose` or `finally`.
- When touching release scripts, validate shell syntax and run the script with a safe temporary output directory.

## Common Maintenance Rules

- Preserve user changes in a dirty worktree.
- Do not reintroduce Epic-specific modules or docs unless the product scope explicitly changes.
- Keep Linux and Windows behavior aligned through AppCore first; platform projects should adapt only the shell and OS integrations.
- If a workflow command uses `--no-restore` with a runtime identifier, ensure the project declares the needed `RuntimeIdentifiers` or the workflow performs a matching restore.

## Important Files

| File | Purpose |
| --- | --- |
| `SteamBacklogPicker.sln` | Solution entrypoint |
| `Directory.Build.props` | Shared MSBuild properties |
| `Directory.Build.targets` | Windows targeting configuration |
| `scripts/local-pipeline.sh` | Local equivalent of common CI coverage |
| `scripts/package-linux-appimage.sh` | Linux portable package and update feed generation |
| `.github/workflows/dotnet.yml` | PR/push CI |
| `.github/workflows/linux-release.yml` | Linux release packaging |
| `docs/architecture.md` | Current architecture overview |
| `TODO.md` | Backlog items |
| `improvements.md` | Audit findings and remediation plan |
