# improvements.md

## 1. System summary
- SteamBacklogPicker is a .NET 8 desktop app with a Domain selection engine, Steam discovery/parsing infrastructure, Steam client fallback integration, shared AppCore viewmodels/services, WPF Windows UI, and Avalonia Linux UI.
- Main risk surfaces reviewed: persisted selection settings, Steam VDF/appmanifest parsing, native Steam API initialization, cross-platform DI bootstrap, local/CI validation, release packaging scripts, update services, and test drift.
- This pass intentionally avoided new product behavior. Changes were limited to correctness, reliability, dead-code removal, duplicate bootstrap cleanup, and validation/documentation hygiene.

## 2. Conventions
- Categories: Bug, Performance, Security, Duplication, Code Quality, Architecture, Maintainability, Observability, Tests, Dependencies.
- Severities: Critical, High, Medium, Low.
- Finding IDs use `A001`, `A002`, ... . Status is `Fixed in this pass` or `Backlog`.

## 3. Progress Tracking
| Scope | Status | Notes |
| --- | --- | --- |
| Domain selection models and tests | reviewed | Subagent + lead review; fixed category/storefront filtering and atomic settings save. |
| SteamDiscovery, Telemetry, SteamClientAdapter, SteamHooks, ValveFormatParser and tests | reviewed | Subagent + lead review; fixed path dedupe, manifest race handling, Steam VDF fallback, native reset. |
| AppCore, WPF UI, Linux UI and presentation tests | reviewed | Subagent + lead review; fixed shared bootstrap, stale test fakes, obsolete runtime links. |
| Root docs, workflows, scripts, project files, manifests | reviewed | Subagent + lead review; fixed local pipeline drift and TODO/.gitignore hygiene. |
| Dependency/CVE audit | completed | `dotnet list SteamBacklogPicker.sln package --vulnerable --include-transitive` is clean after conservative package updates. |

## 4. Complete finding inventory

### A001
Category: Bug  
Severity: Medium  
Status: Fixed in this pass
Location: `src/Domain/SelectionEngine/SelectionFilters.cs:39-47`, `src/Domain/SelectionEngine/SelectionEngine.cs:237-244`  
Reachability: Normal user toggles all category filters off in WPF/Avalonia; viewmodel writes an empty category list.  
Problem: Empty category selection was normalized back to `Game`, so the user could not produce "no category selected".  
Impact: Draw/filter results still included games after all category toggles were disabled.  
Suggestion: Preserve explicit empty category lists while still defaulting null legacy settings to `Game`. Added domain regression coverage.  
Correlation notes: Covered by `FilterGames_ShouldReturnNoMatches_WhenAllCategoriesAreExcluded`.

### A002
Category: Bug  
Severity: Medium  
Status: Fixed in this pass
Location: `src/Domain/SelectionEngine/SelectionFilters.cs:17-30`, `src/Presentation/SteamBacklogPicker.AppCore/ViewModels/SelectionPreferencesViewModel.cs:160-168`  
Reachability: Normal user unchecks the Steam storefront filter.  
Problem: Empty storefront list previously meant "no filter", so unchecking Steam still allowed Steam entries.  
Impact: Storefront filter UI could not exclude the only storefront.  
Suggestion: Added explicit `FilterByStorefront` so legacy empty settings remain unfiltered while user-authored empty selections mean no storefronts.  
Correlation notes: Covered by Domain and SelectionPreferencesViewModel tests.

### A003
Category: Bug  
Severity: Medium  
Status: Fixed in this pass
Location: `src/Domain/SelectionEngine/SelectionEngine.cs:144-170`  
Reachability: Normal `PickNext`, `UpdatePreferences`, and `ClearHistory` persistence.  
Problem: Settings were written with `File.Create`, truncating the existing file before JSON serialization completed.  
Impact: Process interruption during save could lose preferences/history and reload defaults.  
Suggestion: Write to a temp file, flush to disk, then replace the target.  
Correlation notes: Covered by `UpdatePreferences_ShouldPersistSettingsWithoutLeavingTemporaryFiles`.

### A004
Category: Duplication  
Severity: Medium  
Status: Fixed in this pass
Location: `src/Presentation/SteamBacklogPicker.AppCore/Composition/ApplicationCoreServiceCollectionExtensions.cs:17-61`, `src/Presentation/SteamBacklogPicker.Linux/App.axaml.cs:73-79`, `src/Presentation/SteamBacklogPicker.UI/App.xaml.cs:110-112`  
Reachability: Normal WPF/Linux startup and Linux presentation tests.  
Problem: Steam/parsing/cache/viewmodel DI bootstrap was duplicated between WPF and Linux, and Linux startup also repeated registration after calling its bootstrap extension.  
Impact: Divergent registrations and duplicate providers could make Linux load the same Steam library twice or drift from WPF behavior.  
Suggestion: Centralized shared application services in AppCore and left platform-specific UX services in the platform projects.  
Correlation notes: Removed obsolete linked runtime files and `LinuxSteamRegistryReader`.

### A005
Category: Bug  
Severity: Medium  
Status: Fixed in this pass
Location: `src/Infrastructure/SteamDiscovery/SteamLibraryFoldersParser.cs:8-42`  
Reachability: Linux library discovery with two valid Steam library paths that differ only by case.  
Problem: Library folder dedupe used ordinal-ignore-case comparison for all platforms.  
Impact: One Linux library could be dropped, hiding installed games.  
Suggestion: Inject/use `IPathComparisonStrategy` for platform-aware dedupe.  
Correlation notes: Covered by case-sensitive/case-insensitive parser tests.

### A006
Category: Bug  
Severity: Medium  
Status: Fixed in this pass
Location: `src/Infrastructure/SteamDiscovery/SteamAppManifestCache.cs:121-175`, `src/Infrastructure/SteamDiscovery/SteamAppManifestCache.cs:146-158`  
Reachability: Normal refresh while Steam library folders are remounted, unplugged, permission-changed, or manifests are being rewritten.  
Problem: Manifest enumeration could throw outside a guard, and watcher change events removed entries on transient read/parse failure.  
Impact: Library refresh could fail or games could disappear until a later successful event.  
Suggestion: Guard enumeration and keep existing cache entries on transient watcher read failures.

### A007
Category: Bug  
Severity: Medium  
Status: Fixed in this pass
Location: `src/Integration/SteamClientAdapter/SteamVdfFallback.cs:113-116`, `src/Integration/SteamClientAdapter/SteamVdfFallback.cs:158-161`, `src/Integration/SteamClientAdapter/SteamVdfFallback.cs:254-257`, `src/Integration/SteamClientAdapter/SteamVdfFallback.cs:787-805`  
Reachability: Normal library load against real Steam config files that are corrupt, partially written, permission-blocked, or format-drifted.  
Problem: Text VDF config reads/parses were not isolated.  
Impact: Fallback discovery could throw instead of returning partial/empty metadata.  
Suggestion: Added guarded text VDF parsing and malformed loginusers regression coverage.

### A008
Category: Bug  
Severity: Low  
Status: Fixed in this pass
Location: `src/Integration/SteamClientAdapter/SteamClientAdapter.cs:63-80`, `src/Integration/SteamClientAdapter/SteamClientAdapter.cs:183-204`  
Reachability: Native Steam API partially initializes and later accessor/state validation fails.  
Problem: Reset could free the native library without calling `SteamAPI_Shutdown` after successful init.  
Impact: Native Steam API state could leak and retries could behave inconsistently.  
Suggestion: Track successful Steam API startup and call shutdown during reset before freeing the library handle.

### A009
Category: Tests  
Severity: Medium  
Status: Fixed in this pass
Location: `scripts/local-pipeline.sh:4-32`  
Reachability: Contributor/release maintainer follows repo guidance to verify locally.  
Problem: Local pipeline restored and ran only three test projects while CI builds more common projects and runs SteamHooks/Linux presentation tests.  
Impact: Release-impacting failures could pass local verification.  
Suggestion: Align local pipeline with the CI common build/test surface.

### A010
Category: Maintainability  
Severity: Low  
Status: Fixed in this pass
Location: `.gitignore`, `TODO.md`, removed duplicate/obsolete files under presentation services.  
Reachability: Normal maintenance and future agent work.  
Problem: Obsolete runtime/update duplicate files and completed TODO entries kept stale guidance in the tree.  
Impact: Maintainers could edit dead files or repeat completed work.  
Suggestion: Removed obsolete files, removed completed TODO entries, and ignored `codex-scripts/`.

### A011
Category: Security  
Severity: High  
Status: Fixed in this pass  
Location: test project files, `src/Presentation/SteamBacklogPicker.UI/SteamBacklogPicker.UI.csproj`, `src/Presentation/SteamBacklogPicker.Linux/SteamBacklogPicker.Linux.csproj`, `src/Presentation/SteamBacklogPicker.AppCore/SteamBacklogPicker.AppCore.csproj`  
Reachability: Restore/build/test and release dependency graph.  
Problem: NuGet vulnerability audit found vulnerable transitives in test projects, the WPF UI, and the Linux app.  
Impact: Known vulnerable packages could ship or stay hidden in CI/test artifacts.  
Suggestion: Updated test tooling within the current major lines, updated Avalonia within 11.x, updated Microsoft.Extensions packages within 8.x, and pinned `System.Drawing.Common` to the current 8.x patched line for the WPF notification dependency.  
Correlation notes: `dotnet list SteamBacklogPicker.sln package --vulnerable --include-transitive` reports no vulnerable packages.

### A012
Category: Maintainability
Severity: Medium
Status: Fixed in this pass
Location: `AGENTS.md:9-16`, `README.md:34-35`, `.github/workflows/dotnet.yml:191-204`
Reachability: Release maintainer follows release docs.
Problem: Windows packaging docs mention `build/package-msix.ps1`, MSIX/Squirrel/Winget automation, and installers, but no `build/` scripts are tracked and CI uploads raw publish output.
Impact: Release operators and users can be misled about available deliverables.
Suggestion: Either restore packaging automation or update docs to describe the actual artifact.
Correlation notes: README, AGENTS, CLAUDE and release runbook now describe current raw Windows publish output and Linux portable package.

### A013
Category: Maintainability
Severity: Medium
Status: Fixed in this pass
Location: `scripts/package-linux-appimage.sh:18-46`, `.github/workflows/linux-release.yml:36-47`, `README.md:50-63`
Reachability: Linux release workflow.
Problem: Linux artifact is named `.AppImage` but produced by copying a self-contained executable; script says "AppImage placeholder".
Impact: Release metadata can overpromise an AppImage-compatible package.
Suggestion: Build a real AppImage or rename/document the artifact as a portable Linux executable.
Correlation notes: First pass renamed the local fallback to `SteamBacklogPicker-<version>-linux-x64`; A020 later added native AppImage generation for the release path.

### A014
Category: Security
Severity: Low
Status: Fixed in this pass
Location: `.github/workflows/dotnet.yml:13-17`
Reachability: Normal push/PR CI.
Problem: CI grants `checks: write` and `pull-requests: write` although reviewed jobs build/test/upload artifacts only.
Impact: Unused write scopes widen the blast radius if workflow execution is abused.
Suggestion: Reduce permissions to `contents: read` unless write scopes are reintroduced with a specific step.
Correlation notes: CI permissions now request only `contents: read`.

### A015
Category: Maintainability
Severity: Low
Status: Fixed in this pass
Location: `docs/architecture.md:21-53`, `CLAUDE.md:136-142`
Reachability: Maintainer/agent follows architecture docs.
Problem: Docs still describe removed EpicDiscovery modules and registration.
Impact: Future cleanup or feature work can be routed toward nonexistent modules.
Suggestion: Update architecture/agent docs to match the Steam-only current source tree.
Correlation notes: Architecture and assistant guide now document the current Steam-only WPF/Avalonia/AppCore layout.

### A016
Category: Duplication
Severity: Low
Status: Fixed in this pass
Location: `tests/Presentation/SteamBacklogPicker.UI.Tests/GameDetailsViewModelTests.cs:58`, `tests/Presentation/SteamBacklogPicker.UI.Tests/MainViewModelTests.cs:164`, `src/Presentation/SteamBacklogPicker.Linux/Composition/ServiceCollectionExtensions.cs:19`, `src/Presentation/SteamBacklogPicker.UI/Composition/ServiceCollectionExtensions.cs:17`
Reachability: Normal test/platform-service maintenance.
Problem: Duplicate test fakes and similar platform UX registration remain after removing the larger bootstrap duplication.
Impact: Small maintenance drag, but lower risk than the removed application bootstrap duplication.
Suggestion: Extract shared test fake or platform UX helper after a successful compile/test cycle.
Correlation notes: Added shared platform UX registrar and shared UI test localization fake.

### A017
Category: Maintainability
Severity: Low
Status: Fixed in this pass
Location: `src/Presentation/SteamBacklogPicker.Linux/Services/Notifications/LinuxToastNotificationService.cs`
Reachability: Normal Linux game draw notification.
Problem: Linux notifications depended on spawning `notify-send`, leaving behavior tied to an external CLI instead of the Freedesktop notification API.
Impact: Missing CLI tools or argument handling drift could silently prevent notifications even when DBus notification support was available.
Suggestion: Call `org.freedesktop.Notifications.Notify` directly through `Tmds.DBus.Protocol`, keep failures optional, and cover the notification payload with unit tests.
Correlation notes: `LinuxToastNotificationServiceTests` covers payload forwarding and backend failure tolerance.

### A018
Category: Security
Severity: Medium
Status: Fixed in this pass
Location: `src/Presentation/SteamBacklogPicker.Linux/Services/Updates/LinuxAppImageUpdateService.cs`, `scripts/package-linux-appimage.sh`, `.github/workflows/linux-release.yml`
Reachability: Linux update feed download.
Problem: Linux updates relied on a SHA-256 value delivered by the same unsigned feed that supplied the download URL.
Impact: A compromised feed could redirect the app to a malicious binary and matching hash.
Suggestion: Support RSA SHA-256 feed signatures over `version`, `downloadUrl`, and `sha256`; accept unsigned feeds only under explicit local opt-in.
Correlation notes: Linux update tests cover signed feed success, invalid signature rejection, missing public key rejection, hash mismatch, network failure, and rollback.

### A019
Category: Security
Severity: Medium
Status: Fixed in this pass
Location: `src/Presentation/SteamBacklogPicker.UI/Services/Updates/SquirrelUpdateService.cs`, `src/Presentation/SteamBacklogPicker.AppCore/Services/Updates/LegacyWindowsUpdatePolicy.cs`
Reachability: Windows startup inside a legacy Squirrel install.
Problem: The Windows update service could attempt legacy Squirrel/GitHub updates whenever `Update.exe` was present, before the project had an independent Authenticode/certificate pinning strategy.
Impact: Auto-update could be reactivated implicitly by packaging layout before release authenticity policy was ready.
Suggestion: Gate legacy Squirrel updates behind explicit `SBP_ENABLE_LEGACY_WINDOWS_UPDATE` opt-in until signed update verification is provisioned.
Correlation notes: `LegacyWindowsUpdatePolicyTests` covers default-disabled behavior and accepted opt-in values.

### A020
Category: Maintainability
Severity: Medium
Status: Fixed in this pass
Location: `scripts/package-linux-appimage.sh`, `.github/workflows/linux-release.yml`
Reachability: Linux release packaging.
Problem: The Linux release path still produced a portable executable by default even though release docs/backlog targeted a native AppImage package.
Impact: Release artifacts could keep drifting from Linux desktop distribution expectations.
Suggestion: Build a minimal AppDir and use `appimagetool` in CI to produce `SteamBacklogPicker-<version>-linux-x64.AppImage`, while preserving a local portable fallback when the tool is absent.
Correlation notes: Local validation used a fake `appimagetool` to verify AppDir structure, `.AppImage` feed URL, checksum, and feed signature without requiring the native tool on macOS.

### A021
Category: Security
Severity: Low
Status: Fixed in this pass
Location: `src/Integration/SteamHooks/SteamHookClientFactory.cs`, `src/Integration/SteamHooks/SteamMemoryPollingHookClient.cs`, `docs/testing/runbook-installacao-windows-linux.md`
Reachability: Experimental Steam hook memory mode on Linux.
Problem: The backlog still framed `/proc/<pid>/mem` as an undecided default despite the implementation already degrading Linux memory inspection unless unsafe reads are explicitly enabled.
Impact: Future release work could accidentally treat process-memory reads as a normal parity path.
Suggestion: Keep fallback/no-op as the product default, document real-Linux validation as manual release evidence only, and require explicit opt-in for unsafe memory reads.
Correlation notes: `SteamHookClientFactoryTests` covers degradation when `EnableUnsafeLinuxMemoryRead` is false.

## 5. Prioritized backlog
| Priority | Finding | Effort | Rationale |
| --- | --- | --- | --- |
| 1 | A012 | M | Completed: release docs now describe actual artifacts. |
| 2 | A013 | M/L | Completed: Linux package naming now matches actual deliverable. |
| 3 | A014 | S | Completed: CI permissions reduced. |
| 4 | A015 | M | Completed: architecture guidance matches current source tree. |
| 5 | A016 | S | Completed: shared helper/fake removed targeted duplication. |
| 6 | A017 | S | Completed: Linux notifications now use direct Freedesktop DBus calls. |
| 7 | A018 | M | Completed: Linux update feeds can now be signed and unsigned feeds require explicit opt-in. |
| 8 | A019 | S | Completed: legacy Windows Squirrel auto-update is disabled by default. |
| 9 | A020 | M | Completed: Linux release workflow now produces native AppImage when appimagetool is available. |
| 10 | A021 | S | Completed: Linux memory hook default is documented as fallback/no-op, with real-Linux validation kept manual. |

## 6. Detailed phased remediation plan
Phase 1 - Compile/test gate  
- Completed on macOS for cross-platform targets: `dotnet restore SteamBacklogPicker.sln`, `scripts/local-pipeline.sh`, `dotnet build SteamBacklogPicker.sln -c Release --no-restore`, and Windows-targeted SteamDiscovery tests.
- WPF Windows tests compile but do not execute on macOS because `Microsoft.WindowsDesktop.App` is not available there; run them on Windows before release.

Phase 2 - Release truth cleanup  
- Completed: README, AGENTS, CLAUDE, runbook and Linux packaging script describe the same current deliverables.
- Validation: dry-run packaging command generated a portable Linux package and feed with the expected artifact URL.

Phase 3 - CI hardening  
- Completed: A014 lowered CI permissions to read-only contents.
- Validation: workflow diff checked and final CI-equivalent build/test commands run locally.

Phase 4 - Documentation and small duplication  
- Completed: A015 and A016 after successful build baseline.
- Validation: targeted Linux tests and Windows-targeted WPF test build passed.

Phase 5 - Linux UX parity cleanup
- Completed: Linux notifications no longer shell out to `notify-send`; the platform project owns a direct Freedesktop DBus notification client.
- Validation: Linux presentation tests cover notification payload forwarding, backend failure tolerance, DI registration, and existing update/UI flows.

Phase 6 - Linux update authenticity
- Completed: Linux update feed validation supports RSA SHA-256 signatures and rejects unsigned feeds unless `SBP_ENABLE_UNSIGNED_LINUX_UPDATE_FEED=true`.
- Validation: targeted update tests cover signed success, bad signature rejection, unsigned opt-in behavior, network/hash failures, and rollback.

Phase 7 - Windows update safety
- Completed: legacy Squirrel auto-update is disabled by default and requires explicit `SBP_ENABLE_LEGACY_WINDOWS_UPDATE` opt-in.
- Validation: shared policy tests execute on the Linux test target; WPF test project compiles on macOS but cannot execute without `Microsoft.WindowsDesktop.App`.

Phase 8 - Native Linux packaging
- Completed: Linux packaging creates an AppDir and uses `appimagetool` in CI to emit a native `.AppImage`; local runs without the tool still produce the portable executable fallback.
- Validation: script syntax passed, workflow YAML parsed, and a fake `appimagetool` run verified the AppDir contract, generated `.AppImage` path, feed URL, SHA-256, and signature.

Phase 9 - Linux memory-hook boundary
- Completed: release runbook now records the expected `/proc/<pid>/mem` validation matrix while preserving fallback/no-op as the safe default.
- Validation: existing SteamHooks factory tests cover Linux degradation when unsafe memory reads are disabled.

## 7. Completeness checkpoint
- Security review found no hardcoded secrets in the reviewed source/config/test surface.
- Major bootstrap duplication was removed; remaining duplicate names are low-risk platform/test helpers.
- Static checks run successfully: `bash -n scripts/local-pipeline.sh`, `bash -n scripts/package-linux-appimage.sh`, `git diff --check`, and stale-reference `rg` searches.
- Full validation run successfully on this machine where supported: local pipeline, Release solution build, Windows-targeted SteamDiscovery tests, and NuGet vulnerability audit.
- WPF UI tests could not execute on macOS because the Windows Desktop runtime is unavailable outside Windows; the WPF project and test project still compile in Release.
- Files changed intentionally are code/tests/scripts/docs hygiene plus this report; no unrelated user changes were reverted.
