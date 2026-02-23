# CHANGES

- Corrigi o fallback de capa na UI Linux: a mensagem `GameDetails_NoCoverSubtitle` agora só aparece quando `SelectedGame.CoverImagePath` está vazio e deixa de sobrepor artes válidas.
- Adicionei converter + testes de apresentação/conversão para garantir a visibilidade condicional de imagem e mensagem de fallback.

- Mapeei a paridade visual WPF→Avalonia na tela principal Linux, expandi os blocos de filtros/status/detalhes com bindings do `MainViewModel` e conectei atualização dinâmica de recursos de idioma no bootstrap Avalonia.
- Adicionei testes de apresentação no cliente Linux cobrindo jornadas mínimas de abrir, filtrar, sortear e acionar launch/install, além de checklist atualizado em `docs/testing/journey-parity-checklist.md`.

- Ajustei o bootstrap Linux para registrar `ISteamEnvironment`, padronizar `ISteamInstallPathProvider` com `DefaultSteamInstallPathProvider`, construir `ISteamVdfFallback` via `environment.GetSteamDirectory()` e tentar inicializar a Steam API ao criar `ISteamClientAdapter`.
- Adicionei testes de composição Linux cobrindo tentativa de init da API no bootstrap e fallback de manifestos VDF quando a biblioteca nativa não é encontrada.

- Introduzi `IPathComparisonStrategy` no `SteamDiscovery` para comparação de paths sensível à plataforma (Windows case-insensitive, Linux case-sensitive) e apliquei em `SteamLibraryLocator` e caches/lookups de manifests para evitar colisões indevidas por case.
- Adicionei testes parametrizados por plataforma simulada para validar colisão de paths, comparação em `FilePathMatches` e refresh após rename com diferença apenas de caixa.

- Atualizei a descoberta de instalação Steam no Linux com prioridade documentada (`STEAM_PATH` > caminhos tradicionais incluindo `~/.steam/debian-installation` > Flatpak/Snap) mantendo validação por `steamapps/libraryfolders.vdf` e cobrindo novos cenários em testes.
- Corrigi a documentação de distribuição Linux para refletir o estado real da automação: não há pipeline de release Linux gerando artefatos instaláveis no repositório.
- Ajustei requisitos/runbook para orientar execução local no Linux até a esteira de empacotamento Linux ser implementada.

- Atualizei a documentação de portabilidade com requisitos, instalação/execução paralela Windows/Linux, critérios de paridade por jornada e runbooks manuais equivalentes em `docs/testing`.
- Padronizei a convenção de changelog de portabilidade com data-alvo de convergência e alinhei o `docs/user-guide.md` para manter a UX unificada entre plataformas.

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

