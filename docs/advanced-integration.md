# Advanced Integration Research

This document captures the current research spike for optional, high-risk Steam integrations. None of the approaches below are enabled by default in SteamBacklogPicker; they are prototypes meant to inform future work.

## 1. Hooking into `steam.exe` download events

### Findings
- The legacy Steam UI exposes an undocumented bidirectional named pipe (commonly `steam.ipc` or `steam.pipe`) that relays download telemetry to the Chromium-based front-end. It is technically feasible to attach to that pipe and mirror the event stream (appid, depotid, state transitions and byte counters). However, the protocol is not versioned, message formats change without notice, and the pipe is not available when the client falls back to the "minimal" bootstrapper.
- Modern clients multiplex multiple transports (named pipes, shared memory regions, protobuf blobs) depending on feature flags. Hooking requires heuristics to select the correct IPC surface at runtime, making the solution brittle across Steam beta/stable channels.
- Valve’s subscriber agreement and VAC policies prohibit tampering with the client binary. A passive listener that only opens documented Windows handles is less risky, but any memory patching or code injection is likely to be flagged. The prototype must remain read-only and operate without DLL injection to reduce ban risk.
- Steam already logs download completions into `logs/content_log.txt` and updates manifest files under `steamapps`. For product-grade telemetry, periodically tailing those files (or consuming the Web API for owned apps) is a lower-risk alternative.

### Limitations
- Pipe presence is not guaranteed: family sharing, offline mode, or a future UI rewrite can remove it entirely.
- There is no stable correlation identifier between pipe events and the manifest data. Consumers must reconcile using `appid`/`depotid` pairs.
- Without official documentation the hook cannot be supported under Valve’s terms. Ship as opt-in, experimental functionality only.

## 2. Prototype module (`src/Integration/SteamHooks`)

Two experimental implementations were added:

1. **`SteamNamedPipeHookClient`** – Connects to an IPC pipe, performs the same handshake as the web UI, and parses TSV payloads into strongly typed `SteamDownloadEvent` records. It automatically retries after disconnects and filters events by `AppId` when requested.
2. **`SteamMemoryPollingHookClient`** – Polls the `steam.exe` process for read-only access, scanning configured memory addresses and parsing textual snapshots. The class is intentionally conservative: it only requests `PROCESS_VM_READ`/`PROCESS_QUERY_INFORMATION`, supports opt-in address lists, and never mutates process memory.

A `SteamHookClientFactory` chooses the strategy based on `SteamHookOptions`, making the module easy to guard behind configuration flags. The project is not referenced elsewhere to avoid accidental activation.

### Outstanding work
- Reverse-engineer up-to-date message schemas for both the pipe and memory snapshots, ideally under controlled lab conditions with multiple client branches.
- Replace the ad-hoc TSV parser with protobuf/FlatBuffer decoding if Valve reuses the `ClientUpdate` protocol buffers found in `steamclient.dll`.
- Harden reconnection logic (exponential backoff, jitter) and add structured logging hooks.
- Provide sandboxed integration tests that replay captured IPC traffic without touching the real client.

## 3. Overlay via WebView2

### Concept
Create an auxiliary transparent window that uses WebView2 to render SteamBacklogPicker UI elements (e.g., download completion toasts) on top of the Steam window. Steam’s own overlay is a separate process (`GameOverlayUI.exe`) that injects DirectX hooks; duplicating that behavior is risky. A lighter alternative is to:

1. Spawn a layered, click-through window with `WS_EX_LAYERED | WS_EX_TRANSPARENT` flags.
2. Host WebView2 inside that window to render HTML/React components served by the existing UI project.
3. Track the Steam client window rectangle via `GetWindowRect` + `SetWindowPos` and reposition the overlay accordingly.

### Risks & Maintenance Costs
- WebView2 requires the Evergreen runtime (~120 MB) on every target machine; shipping a fixed version increases installer size and needs periodic security updates.
- Transparent WebView2 surfaces use GPU-accelerated composition. On systems with older Intel drivers the combination of Steam’s hardware acceleration and the overlay can lead to flicker or increased GPU usage.
- Overlay synchronization is fragile: Steam can spawn child windows (Big Picture, downloads view) causing focus and Z-order issues. Additional accessibility work is required to ensure the overlay does not intercept input or block screen readers.
- Anti-cheat implications: while this approach avoids DLL injection, repeatedly querying and repositioning the Steam window may still be flagged in competitive games launched through Steam, especially if combined with memory hooks.

### Alternatives
- Rely on the native Steam overlay (`ISteamFriends::ActivateGameOverlayToWebPage`) to present web content instead of maintaining a custom overlay.
- Use Windows toast notifications (WinRT) triggered by download completions instead of maintaining a persistent overlay window.

## 4. Summary of current boundaries
- The hook prototype is purely observational and off by default. Enabling it requires explicit configuration and carries risk of breaking whenever Valve updates the client.
- No persistent background service is created; all handles are closed once the subscription ends.
- Further progress depends on legal review of Valve’s terms, stability testing against Steam beta builds, and user opt-in flows that highlight the experimental nature of the feature.
