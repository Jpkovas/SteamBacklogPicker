# SteamBacklogPicker – User Guide

## Getting started

1. **Install the application** using the MSIX package, Winget, or Squirrel bootstrapper as described in the [README](../README.md).
2. Launch SteamBacklogPicker from the Start Menu. On first launch the app scans your Steam libraries; this may take a few minutes for very large collections.
3. When prompted, decide whether you want to share anonymous telemetry. Telemetry is optional and can be toggled later.

## Navigating the interface

- **Library overview** – Displays the total number of detected titles, filters, and quick actions such as refreshing manifests.
- **Game card** – Shows the game selected by the backlog engine, including cover art, tags, playtime, and install status.
- **Filters panel** – Use the checkboxes and sliders to prioritise installed games, exclude multiplayer-only titles, or boost favourites.
- **Action buttons** –
  - `Roll Again` triggers another random selection using the configured weights.
  - `Launch in Steam` opens the selected game directly through the Steam client.
  - `Open install folder` jumps to the game directory when it is present locally.

## Managing telemetry and logs

- Open the **Settings** panel from the gear icon.
- Under **Privacy**, toggle the **Share anonymous telemetry** switch.
- The current log files live in `%LOCALAPPDATA%\SteamBacklogPicker\logs`. Use the `Export diagnostics` button to create a ZIP bundle for support.

## Updating the application

- **MSIX installations** are serviced by Windows Update or the Microsoft Store (if sideloaded via Store submission).
- **Squirrel installations** poll the release feed during start-up. Keep the app running to allow background download; the update is applied the next time the app launches.
- **Winget** users can run `winget upgrade Contoso.SteamBacklogPicker` to fetch the latest signed package.

## Troubleshooting

- If SteamBacklogPicker cannot find your libraries, open **Settings → Steam locations** and add custom library paths.
- When the Steam API is not available, the app falls back to the cached manifests stored in `%LOCALAPPDATA%\SteamBacklogPicker\cache`.
- Review the latest log file and share it with support if you encounter crashes or repeated selection failures.
- Reset preferences by deleting `%LOCALAPPDATA%\SteamBacklogPicker\telemetry-consent.json` and `%APPDATA%\SteamBacklogPicker\settings.json` while the app is closed.

## Support

For bug reports or feature suggestions open an issue on the project repository. Attach diagnostics if possible and include the application version shown on the splash screen.

