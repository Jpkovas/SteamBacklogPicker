# Journey parity checklist (Windows WPF x Linux Avalonia)

Este checklist garante que os dois clientes de apresentação usam os mesmos fluxos do `AppCore`.

| Etapa | Windows (`SteamBacklogPicker.UI`) | Linux (`SteamBacklogPicker.Linux`) | Evidência |
| --- | --- | --- | --- |
| Abrir app | ✅ | ✅ | Ambos resolvem `MainViewModel` via DI no startup. |
| Carregar biblioteca | ✅ | ✅ | Ambos usam `IGameLibraryService` + `CombinedGameLibraryService`. |
| Filtrar biblioteca | ✅ | ✅ | Ambos usam `SelectionPreferencesViewModel` do `AppCore`. |
| Sortear jogo | ✅ | ✅ | Ambos acionam `DrawCommand` do `MainViewModel`. |
| Abrir jogo | ✅ | ✅ | Ambos usam `IGameLaunchService` compartilhado. |
| Notificação | ✅ toast nativo | ✅ noop (placeholder) | Contrato `IToastNotificationService` compartilhado; Linux possui implementação substituível. |
| Atualização | ✅ Squirrel | ⚠️ não implementado | Linux ainda não possui serviço de atualização equivalente. |

## Validação executada nesta entrega

1. Conferência estática da composição de DI no bootstrap WPF e Linux.
2. Revisão dos contratos compartilhados no `SteamBacklogPicker.AppCore`.
3. Verificação de solução/projetos para garantir referência dos dois front-ends ao `AppCore`.
