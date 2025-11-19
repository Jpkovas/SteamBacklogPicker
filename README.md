# SteamBacklogPicker

SteamBacklogPicker is a WPF desktop app that helps you pick the next game from your Steam and Epic libraries without relying on any cloud services. The application reads data directly from the Steam client and Epic Games Launcher caches stored on your PC, so everything stays on your machine and works even when you are offline.

## Key capabilities

- **Unified Steam + Epic library** – discover, filter, and draw from both storefronts at once, including shared Steam Family libraries.
- **Offline-first metadata** – Steam manifests, Epic catalog/entitlement caches, and the built-in hero art downloader are mirrored locally so cover art and install status remain available without a network connection.
- **Collection-aware filtering** – constrain the picker with Steam collections, tags, installed status, storefront, or any mix of the above before spinning.
- **Actionable selection cards** – each draw highlights install state, storefront context, and launchability so you know whether you can jump in immediately.
- **Epic Games integration without authentication** – manifests, catalog JSON, SQLite caches, and GraphQL metadata are parsed locally; no Epic credentials ever leave your machine.
- **Structured diagnostics** – opt-in telemetry emits anonymised usage events, while detailed logs are written under `%LOCALAPPDATA%\SteamBacklogPicker\logs` for troubleshooting.
- **Modern UI polish** – responsive dark theme, cover art overlays, and an animated “Sorteando” state keep the picker fun to use.
- Weighted randomisation and long-form descriptions remain on the roadmap and will arrive in a future update.

## System requirements

- Windows 10 21H2 (build 19044) or newer / Windows 11 (build 22000+) with .NET 8 Desktop Runtime.
- 64-bit CPU with support for AVX.
- 4 GB RAM and 512 MB free storage.
- Steam client installed with access to the `steamapps` manifest files.
- (Optional) Epic Games Launcher installed so the app can read `%PROGRAMDATA%\Epic\EpicGamesLauncher\Data\Manifests` and `%LOCALAPPDATA%\EpicGamesLauncher\Saved\Data\Catalog` / `%APPDATA%\Epic\EpicGamesLauncher\Saved\Data\Catalog` caches when available.

## Installation

1. Download the latest `Setup.exe` from the SteamBacklogPicker releases page.
2. Run the installer. Squirrel will place the app under `%LOCALAPPDATA%\SteamBacklogPicker` and create shortcuts for you.
3. Future updates are applied automatically whenever a new release is published.

## Building from source

1. Install the .NET 8 SDK and Visual Studio 2022 with WPF tools.
2. Clone the repository and restore dependencies:
   ```powershell
   git clone https://github.com/Jpkovas/SteamBacklogPicker.git
   cd SteamBacklogPicker
   dotnet restore
   ```
3. Run the UI project:
   ```powershell
   dotnet run --project src/Presentation/SteamBacklogPicker.UI/SteamBacklogPicker.UI.csproj
   ```

## Steam and Epic discovery details

- SteamBacklogPicker reads the same manifest directories (`steamapps`, Epic `.item` manifest files, and catalog SQLite caches) that the official launchers maintain. Paths can be overridden through configuration when you keep your library on another drive.
- Epic entries continue to resolve hero art offline thanks to the bundled catalog key image cache and composite art locator. When a hero image is missing locally the app falls back to Steam's CDN or Epic's catalog metadata depending on the storefront.
- The Epic discovery client hydrates metadata through local caches first and only hits GraphQL endpoints when no cached entitlement exists, keeping the app functional even when the Epic launcher is closed.
- When the launcher relocates its cache folders you can point SteamBacklogPicker at the new location via the `EpicDiscovery` options in `appsettings.json`.

## Telemetry and privacy

Telemetry is disabled by default. When a user enables telemetry, only anonymised usage events (such as application start, selection success, and unhandled exceptions) are captured. Logs are written locally under `%LOCALAPPDATA%\SteamBacklogPicker\logs` and can be purged by the user at any time. No Steam or Epic credentials are collected, and Epic discovery uses cached manifests/catalog entries instead of authenticated APIs.
