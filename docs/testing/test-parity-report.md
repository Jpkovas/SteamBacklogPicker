# Test parity report by layer

| Layer | Linux (common suite) | Windows (common suite) | Windows-only suite |
| --- | --- | --- | --- |
| Domain | `Domain.Tests` | `Domain.Tests` | N/A |
| Infrastructure | `SteamDiscovery.Tests` (`net8.0`) | `SteamDiscovery.Tests` (`net8.0`) | `SteamDiscovery.Tests` (`net8.0-windows10.0.17763.0`) |
| Integration | `SteamClientAdapter.Tests`, `SteamHooks.Tests` | `SteamClientAdapter.Tests`, `SteamHooks.Tests` | N/A |
| Presentation | `SteamBacklogPicker.Linux.Tests` | `SteamBacklogPicker.Linux.Tests` | `SteamBacklogPicker.UI.Tests` (`net8.0-windows10.0.18362.0`) |

Este relatório garante visibilidade explícita de cobertura por sistema operacional e ajuda a prevenir regressões de paridade entre Linux e Windows na esteira de CI.
