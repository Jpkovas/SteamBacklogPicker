# SteamBacklogPicker

SteamBacklogPicker is a Windows desktop application that helps you decide what to play next from your Steam library. It analyses ownership data, filters out installed or hidden titles, and presents a fair lottery to discover a game you may have forgotten about.

## System requirements

- Windows 10 21H2 (build 19044) or newer / Windows 11 (build 22000+) with .NET 8 Desktop Runtime.
- 64-bit CPU with support for AVX.
- 4 GB RAM and 512 MB free storage.
- Steam client installed with access to the `steamapps` manifest files.

## Installation

1. Baixe `Setup.exe` na página de releases do SteamBacklogPicker.
2. Execute o instalador. O Squirrel instalará o aplicativo em `%LOCALAPPDATA%\SteamBacklogPicker` e criará os atalhos.
3. As atualizações futuras serão aplicadas automaticamente sempre que um novo release estiver disponível.

## Functional scope

- Discover Steam libraries by reading Steam installation paths and `libraryfolders.vdf` metadata.
- Cache application manifests for fast start-up and offline access.
- Provide weighted random selection logic so installed, favourited, or recently played titles can be prioritised.
- Display cover art, metadata, and install status for the selected game.
- Emit structured logs and (opt-in) telemetry for troubleshooting and product analytics.
- Integrate with Windows notifications to surface background updates or reminders.

## Building from source

1. Install the .NET 8 SDK and Visual Studio 2022 with WPF tools.
2. Clone the repository and restore dependencies:
   ```powershell
   git clone https://github.com/your-org/SteamBacklogPicker.git
   cd SteamBacklogPicker
   dotnet restore
   ```
3. Run the UI project:
   ```powershell
   dotnet run --project src/Presentation/SteamBacklogPicker.UI/SteamBacklogPicker.UI.csproj
   ```

## Telemetry and privacy

Telemetry is disabled by default. When a user enables telemetry, only anonymised usage events (such as application start, selection success, and unhandled exceptions) are captured. Logs are written locally under `%LOCALAPPDATA%\SteamBacklogPicker\logs` and can be purged by the user at any time.
