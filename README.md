# SteamBacklogPicker

SteamBacklogPicker is a Windows desktop app that helps you pick the next game from your Steam backlog without relying on any cloud services. The application reads data directly from the Steam client installed on your PC, so everything stays on your machine and works even when you are offline.

## Why players use SteamBacklogPicker

- **Runs entirely locally** – library discovery, filtering, and randomisation all happen on your computer; no account credentials or game data ever leave your device.
- **Compatible with Steam Family libraries** – browse and draw from your own library or any family-shared collection that is accessible through your Steam client.
- **Filter using your Steam collections** – target exactly the games you care about by selecting custom collections, tags, or installed status before the picker spins.
- Cache manifest metadata for instant start-up and smooth offline sessions.
- Highlight install status alongside the selected game so you know whether you can jump in immediately.
- Showcase rich cover art, tags, and ownership context for the drawn game, falling back to Steam's CDN when the local cache is missing.
- Enjoy a refreshed dark theme with responsive cards, clearer calls to action, and an animated "Sorteando" overlay while the picker spins.
- Track personal backlog status, session targets, playtime notes, and offline-friendly HowLongToBeat completion estimates alongside every game.
- Weighted randomisation and long-form descriptions remain on the roadmap and will arrive in a future update.
- Emit structured logs and (optional) telemetry to help with troubleshooting.

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

## Telemetry and privacy

Telemetry is disabled by default. When a user enables telemetry, only anonymised usage events (such as application start, selection success, and unhandled exceptions) are captured. Logs are written locally under `%LOCALAPPDATA%\SteamBacklogPicker\logs` and can be purged by the user at any time.
