# Repository Guidelines

## Project Structure & Module Organization
- `src/Domain` holds selection rules and domain models consumed by every layer.
- `src/Infrastructure` parses Steam manifests, caches metadata, and hides Steamworks.NET details.
- `src/Integration` maintains adapters that hydrate the domain from Steam client APIs.
- `src/Presentation/SteamBacklogPicker.UI` contains the WPF shell (MVVM services + viewmodels); XAML resources live alongside the views.
- Tests mirror the production layout under `tests/...`; fixtures and helpers sit in `tests/TestUtilities`.
- `build/` houses packaging automation (MSIX, Squirrel, Winget) and `docs/` captures architecture, testing checklists, and requirements.

## Build, Test, and Development Commands
- `dotnet restore SteamBacklogPicker.sln` restores all projects with the shared props/targets.
- `dotnet build SteamBacklogPicker.sln -c Release --no-restore` validates production-ready output.
- `dotnet test --no-build` runs every test project (xUnit + FluentAssertions).
- `scripts/local-pipeline.sh` reproduces the CI sequence; run it from Git Bash when verifying a PR locally.
- `pwsh build/package-msix.ps1 -CertificatePath <pfx>` publishes and signs MSIX artifacts; populate `SBP_CERTIFICATE_PASSWORD` and `SBP_TIMESTAMP_URL` when needed.

## Coding Style & Naming Conventions
- Target `.NET 8` with `nullable` and implicit usings enabled; prefer file-scoped namespaces and 4-space indentation.
- Use expression-bodied members sparingly; clarity beats terseness in selection logic.
- Classes, records, and public members stay `PascalCase`; locals and parameters use `camelCase`.
- Domain DTOs are `record class` types; keep them immutable and initialized with sensible defaults.
- Tests follow `MethodName_ShouldExpectation` naming and assert through FluentAssertions for readable intent.

## Testing Guidelines
- Cover new domain logic with unit tests in the matching `tests/<Layer>/<Project>.Tests` folder.
- Integration tests under `tests/Integration` rely on canned Steam fixtures; refresh them only when manifest formats change.
- Keep temporary files in `%TEMP%` and delete them in `Dispose` / `finally` blocks (see `SelectionEngineTests.cs`).
- Document exploratory, manual, or performance checks in `docs/testing/` when they influence release decisions.

## Commit & Pull Request Guidelines
- Follow the existing history: short, imperative commits (e.g., `Prevent CRLF corruption of binary Steam fixtures`); avoid ticket prefixes unless linking is required in the body.
- Every PR should describe the change, reference related issues, and mention impacted subsystems (`Domain`, `Infrastructure`, etc.).
- Attach screenshots or logs when touching the UI or installer scripts.
- Ensure the `.NET` GitHub Action passes or include a note explaining any required follow-up before requesting review.
