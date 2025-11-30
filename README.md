# SteamBacklogPicker

SteamBacklogPicker is a WPF desktop app that helps you pick the next game from your Steam library without relying on any cloud services. The application reads data directly from the Steam client stored on your PC, so everything stays on your machine and works even when you are offline.

## Key capabilities

- **Steam library management** – discover, filter, and draw from your Steam library, including shared Steam Family libraries.
- **Offline-first metadata** – Steam manifests and the built-in hero art downloader are mirrored locally so cover art and install status remain available without a network connection.
- **Collection-aware filtering** – constrain the picker with Steam collections, tags, installed status, or any mix of the above before spinning.
- **Actionable selection cards** – each draw highlights install state and launchability so you know whether you can jump in immediately.
- **Structured diagnostics** – opt-in telemetry emits anonymised usage events, while detailed logs are written under `%LOCALAPPDATA%\SteamBacklogPicker\logs` for troubleshooting.
- **Modern UI polish** – responsive dark theme, cover art overlays, and an animated “Sorteando” state keep the picker fun to use.
- Weighted randomisation and long-form descriptions remain on the roadmap and will arrive in a future update.

## System requirements

- Windows 10 21H2 (build 19044) or newer / Windows 11 (build 22000+) with .NET 8 Desktop Runtime.
- 64-bit CPU with support for AVX.
- 4 GB RAM and 512 MB free storage.
- Steam client installed with access to the `steamapps` manifest files.

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

## Steam discovery details

- SteamBacklogPicker reads the same manifest directories (`steamapps`) that the official Steam client maintains. Paths can be overridden through configuration when you keep your library on another drive.
- Hero art is resolved from Steam's local cache and CDN when available.

## Telemetry and privacy

Telemetry is disabled by default. When a user enables telemetry, only anonymised usage events (such as application start, selection success, and unhandled exceptions) are captured. Logs are written locally under `%LOCALAPPDATA%\SteamBacklogPicker\logs` and can be purged by the user at any time. No Steam credentials are collected.
