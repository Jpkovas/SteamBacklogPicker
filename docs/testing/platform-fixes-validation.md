# macOS, telemetry, and Linux update validation

The subsequent full Avalonia/Linux suite on Windows passed **20 tests**, with **11 Linux-only update tests explicitly skipped** (`artifacts/audit/TestResults/avalonia-parity.trx`). The production Avalonia Release build completed with zero warnings and errors. Four tests use the real Avalonia headless control tree and Skia renderer: page navigation/search/filter visibility and keyboard focus, installed/backlog/draw/history bindings, ephemeral QR rendering/clearing, and asynchronous local artwork loading/disposal. The test settings are isolated in temporary directories. This verifies presentation behavior on Windows without opening desktop windows; native Linux desktop interaction still needs its runner/manual smoke check.

The Avalonia shell now shares the Windows workspace commands and presents Discover, Library, History and Settings, with inline advanced filters, virtualized library rows, cancellable cached artwork, and login QR bytes kept only in memory. The headless setup follows the [official Avalonia xUnit guidance](https://docs.avaloniaui.net/docs/testing/headless-xunit).

The Windows host builds the .NET Linux project and runs the telemetry regression test. Targeted result: 1 passed, 11 Linux-only tests explicitly skipped. The extracted swap helper was also run under Git Bash against temporary files: valid bytes replaced the target (exit 0), tampered bytes preserved it (exit 1), and both cases cleaned the marker. Shell syntax checks passed for all three packaging/pipeline scripts and the helper template. Linux updater tests use `LinuxFact`, so Windows reports explicit skips. The Linux CI runner executes the real process/filesystem update tests. macOS CI resolves the official zstd Swift package, builds, runs `swift test`, and packages a development `.app`. Swift/AppKit and the Linux swap process still require their matching runners; a Windows test pass does not prove those runtime paths.

## Reproducible dependencies

- Swift zstd is pinned to 1.5.7 using the [upstream Swift package](https://github.com/facebook/zstd/blob/v1.5.7/Package.swift), product `libzstd`, with commit `f8745da6ff1ad1e7bab384bd1f9d742439278e99` recorded in `Package.resolved`. The decoder uses bounded `ZSTD_decompress`, validates exact output length, and checks the Valve envelope CRC.
- [appimagetool 1.9.1](https://github.com/AppImage/appimagetool/releases/tag/1.9.1), x86_64 asset SHA-256: `ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0`.
- [AppImage type2 runtime 20251108](https://github.com/AppImage/type2-runtime/releases/tag/20251108), x86_64 asset SHA-256: `2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d`.

The two asset digests were read from the official GitHub releases API on 2026-09-06 (`/repos/AppImage/appimagetool/releases/tags/1.9.1` and `/repos/AppImage/type2-runtime/releases/tags/20251108`). CI verifies them before executing the downloaded tool and passes the verified runtime explicitly, avoiding the tool's automatic latest-runtime download.

## Distribution scope

`bash scripts/package-macos-app.sh <numeric-version> <clean-output-directory>` produces a development app ZIP with an ad-hoc signature by default. Supplying `MACOS_SIGNING_IDENTITY` uses an existing local signing identity, but the script does not claim notarization. Public macOS distribution still needs real Developer ID credentials and notarization.

Linux packaging generates a native AppImage when `APPIMAGETOOL_PATH` is supplied; otherwise it labels the single executable as a portable Linux package. Local builds without a published URL emit a checksum and do not invent an update feed URL. Use `SBP_LINUX_DOWNLOAD_URL` to supply a real destination, or publish through the release workflow. Signed feed generation requires the existing private-key secret; unsigned feeds remain rejected by default by the updater. Legacy Squirrel updating remains disabled.

## Important behavior changes

Global appinfo is descriptive metadata, not current-account license evidence. Missing `StateFlags` yields unknown installation; bit 4 supplies installation evidence. Family ownership is independent of installation and old `shared` states never satisfy the installed filter. macOS name resolution uses at most four concurrent requests, a 25-second overall deadline, cancellation, and a seven-day cache scoped to Steam language. Corrupt VDF input is limited by file, payload, depth, node and string budgets.

Pending Linux update markers now retain feed authentication data and hash. At apply time, the updater checks the current executable target, fixed pending path, signature policy, and current pending bytes. The helper copies beside the target, validates that copy's SHA-256, and renames atomically after process exit. Missing authentication data in an old marker causes a fresh download rather than installation.
