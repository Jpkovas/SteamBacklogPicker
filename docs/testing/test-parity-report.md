# Test parity report by layer

| Layer | Linux/common suite | Windows/common suite | Windows-only suite | macOS suite |
| --- | --- | --- | --- | --- |
| Domain | `Domain.Tests` | `Domain.Tests` | N/A | Mirrored in Swift selection tests |
| Infrastructure | `SteamDiscovery.Tests` (`net8.0`) | `SteamDiscovery.Tests` (`net8.0`) | `SteamDiscovery.Tests` (`net8.0-windows10.0.17763.0`) | `SteamLibraryServiceTests`, `VDFParserTests`, `SteamAppInfoParserTests` |
| Integration | `SteamClientAdapter.Tests`, `SteamHooks.Tests` | `SteamClientAdapter.Tests`, `SteamHooks.Tests` | N/A | Local Steam metadata parsing and Store appdetails fallback tests |
| Presentation | `SteamBacklogPicker.Linux.Tests` | `SteamBacklogPicker.Linux.Tests` | `SteamBacklogPicker.UI.Tests` (`net8.0-windows10.0.18362.0`) | `SelectionFilterTests` plus runtime Computer Use checks |

Este relatório garante visibilidade explícita de cobertura por sistema operacional e ajuda a prevenir regressões de paridade entre Linux, Windows e macOS na esteira de CI/manual validation.

## Verificação de paridade 2026-06-10

- Windows-base/common: `Domain.Tests` 14/14, `SteamDiscovery.Tests` net8.0 39/39, `SteamDiscovery.Tests` net8.0-windows10.0.17763.0 41/41, `SteamClientAdapter.Tests` 21/21 e `SteamHooks.Tests` 4/4 passaram fora do sandbox gerenciado.
- Linux: `SteamBacklogPicker.Linux.Tests` 25/25 passou contra os mesmos ViewModels/serviços compartilhados do AppCore usados como base para a paridade desktop.
- macOS: `swift test` 50/50 e `swift build --product SteamBacklogPickerMac` passaram, cobrindo descoberta Steam, filtros, coleções, sorteio seedado compatível com .NET, histórico recente, localização, notificações, refresh, install/play e smoke de renderização SwiftUI.
- Limite do host: a suíte WPF `SteamBacklogPicker.UI.Tests` depende do runtime `Microsoft.WindowsDesktop.App` e deve ser executada no Windows/CI; este macOS não possui esse runtime.
