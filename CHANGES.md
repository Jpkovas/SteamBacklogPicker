# CHANGES

- Separei `SteamMemoryPollingHookClient` por implementação explícita de SO (Windows com `ReadProcessMemory`, Linux com `/proc/<pid>/mem` sob feature flag), adicionei contrato de baixo nível para leitura de memória e degradação controlada com diagnósticos estruturados.
- Adicionei testes independentes de plataforma para parser de snapshot e testes de seleção/degradação da implementação por SO no factory de hooks, além de atualizar `docs/advanced-integration.md` com riscos de compliance/anti-cheat em Windows e Linux.

- Criei o projeto compartilhado `SteamBacklogPicker.AppCore` (net8.0), movendo ViewModels e serviços de UI sem dependência WPF para centralizar fluxos e contratos usados por múltiplos clientes.
- Mantive o cliente WPF como host Windows e adicionei o cliente `SteamBacklogPicker.Linux` (Avalonia) consumindo os mesmos ViewModels/serviços do `AppCore`, além de registrar checklist de paridade em `docs/testing/journey-parity-checklist.md`.
- Substituí o acesso direto ao registry por `ISteamInstallPathProvider` no SteamDiscovery, com providers de Windows (registry) e Linux (STEAM_PATH + heurísticas de diretórios padrão/Flatpak) e integração no `SteamLibraryLocator`.
- Adicionei testes de resolução de path de instalação e cobertura da nova injeção no `SteamLibraryLocator` em `tests/Infrastructure/SteamDiscovery.Tests`.
- Extraí a geração de candidatos de bibliotecas nativas Steam por plataforma para `AppCore`, incluindo caminhos Linux (`libsteam_api.so`/`steamclient.so`) e mantive o fallback explícito de Windows.

- Centralizei o binding de UX por plataforma em extensões de DI (`AddPlatformUserExperienceServices`) e mantive `IToastNotificationService`/`IAppUpdateService` como contrato comum no `AppCore` para Windows e Linux.
- Implementei notificações Linux via `notify-send` (Freedesktop) e atualização para AppImage com etapas de feed, download, validação SHA-256 e aplicação no próximo start via marcador pendente.
- Adicionei testes de composição de DI no cliente Linux validando o binding por plataforma (Linux vs fallback no-op).

- Converti `SteamDiscovery` e seus testes para multi-target (`net8.0` + `net8.0-windows10.0.17763.0`) com implementação Windows-only isolada por TFM, mantendo dependências nativas apenas no target Windows.
- Reestruturei o workflow de CI com matriz `ubuntu-latest` + `windows-latest`, separação entre suíte comum e suíte Windows-only, e publicação de relatório de paridade por camada em `docs/testing/test-parity-report.md`.

