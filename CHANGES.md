# CHANGES

- Adicionei o cliente nativo macOS em SwiftUI em `src/Presentation/SteamBacklogPicker.Mac`, com pacote SwiftPM no repositório para manter Windows, Linux e macOS como três versões separadas.
- Portabilizei para macOS a descoberta local da Steam, parsing de `libraryfolders.vdf`/`appmanifest_*.acf`, hidratação de coleções via `sharedconfig.vdf`, arte local/CDN, filtros, sorteio, histórico persistido e ações `steam://run`/`steam://install`.
- Ampliei a descoberta macOS para combinar manifests instalados, `localconfig.vdf`, `librarycache` e nomes/tipos oficiais de `appcache/appinfo.vdf`, evitando limitar a biblioteca aos jogos instalados; o filtro "Somente instalados" agora usa apenas manifests reais como fonte de instalação.
- Reforcei a cobertura macOS para garantir que flags `Installed=1` vindas do cache de perfil da Steam não transformem apps sem `appmanifest_*.acf` em instalados.
- Alinhei a prioridade de nomes da descoberta macOS ao AppCore: nomes oficiais de `appinfo.vdf` agora enriquecem também manifests instalados, sem transformar todo o cache appinfo em biblioteca disponível.
- Alinhei a ordenação da biblioteca macOS ao provider compartilhado, usando título e desempate por identificador para manter ordem determinística quando há títulos duplicados.
- Adicionei fallback macOS de nomes via `store.steampowered.com/api/appdetails`, com cache local em `Application Support/SteamBacklogPicker`, para reduzir entradas que ficavam como `App <id>` quando a Steam local só tinha playtime/appid.
- Completei a leitura de coleções macOS a partir do cache local da Steam em `cloudstorage/cloud-storage-namespace-1.json`, incluindo coleções dinâmicas como instalados, um jogador, multijogador, VR e compatibilidade Steam Deck.
- Alinhei a avaliação macOS de coleções dinâmicas ao provider compartilhado: grupos vazios em `filterSpec` agora são ignorados, preservando o resultado dos outros grupos válidos da coleção.
- Alinhei também o parser macOS de `filterSpec`: grupos vazios são descartados como no fallback compartilhado, e coleções dinâmicas com todos os grupos vazios não são aplicadas.
- Alinhei o parser macOS de coleções cloud ao fallback compartilhado para aceitar apenas números JSON em `added` e `rgOptions`, evitando que strings numéricas criem membros/opções só no cliente macOS.
- Cobri a descoberta macOS de jogos via compartilhamento familiar, preservando `OwnershipType.familyShared` e `InstallState.shared` tanto para manifests com proprietário diferente quanto para entradas disponíveis vindas do metadata da Steam.
- Ampliei a descoberta local da Steam no fallback compartilhado e no cliente macOS para ler árvores reais de `localconfig.vdf` em profundidade e incluir entradas de Steam Family sinalizadas em `appcache/appinfo.vdf`, sem exigir chave de API ou configuração do usuário.
- Recriei a tela principal em SwiftUI com header, seletor PT/EN, sidebar de filtros, status, painel de arte, botão de instalar e botão de jogar, mantendo a composição visual das versões Windows/Linux.
- Ajustei a sidebar macOS para colocar o seletor de coleção dentro da área rolável de filtros, separar melhor as ações fixas de atualizar/sortear e alinhar paddings/largura do dropdown à composição Windows/Linux.
- Compactei o header macOS, troquei o seletor de coleção por um menu de largura total e limitei o redimensionamento máximo da janela ao tamanho visual validado para evitar layouts excessivamente esticados.
- Adicionei rótulos e identificadores de acessibilidade no macOS para os mesmos controles principais expostos pela automação do WPF: painéis, idioma, filtros, seleção de coleção, status, título selecionado, instalação, instalar, jogar e overlay de sorteio.
- Alinhei o estado vazio dos detalhes macOS ao `GameDetailsViewModel` compartilhado: o prompt de sorteio fica no placeholder de arte, enquanto a linha de metadados mostra o estado de instalação desconhecido.
- Alinhei os chips de tags no painel de detalhes macOS ao `GameDetailsViewModel`: tags vazias são removidas, espaços extras são aparados e duplicatas por capitalização aparecem apenas uma vez.
- Corrigi cortes e espaçamentos da UI macOS: o header não compete mais com o título nativo da janela, o painel de arte ajusta altura conforme a janela, os chips de coleção não invadem os botões e os botões Install/Play ficam agrupados sem espaçamento excessivo.
- Troquei a inicialização macOS para um ciclo AppKit explícito hospedando a tela SwiftUI em `NSHostingView`, preparando o bundle manual para abrir a janela principal sem depender do `WindowGroup` do SwiftUI.
- Ajustei a renderização de arte no macOS para carregar arquivos locais da Steam com `NSImage` e usar carregamento assíncrono apenas para capas remotas, mantendo o comportamento equivalente aos paths locais usados por WPF/Avalonia.
- Alinhei os fallbacks remotos de arte do macOS à UI WPF, incluindo a tentativa final `library_600x900.jpg` após header, capsule e SteamDB.
- Alinhei a matriz de ações macOS com o AppCore: Play só fica disponível para jogos instalados e Install cobre jogos disponíveis, compartilhados ou com estado desconhecido.
- Cobri a limpeza da seleção macOS quando uma alteração de filtros torna o jogo selecionado inelegível, preservando o comportamento do `MainViewModel` compartilhado.
- Alinhei também as mensagens de falha de Play/Install no macOS com o `GameLaunchService` compartilhado, diferenciando loja não suportada, appid Steam ausente, jogo não instalado e jogo já instalado.
- Alinhei a execução de links Steam no macOS ao tratamento de falha das outras plataformas: se o sistema não conseguir abrir `steam://run` ou `steam://install`, o erro aparece no status e é registrado no diagnóstico local.
- Alinhei o texto de estado de instalação macOS ao `GameDetailsViewModel`: jogos `Available` vindos de compartilhamento familiar agora mostram `Disponível via compartilhamento familiar`.
- Cobri e normalizei os filtros macOS de tipo de conteúdo: jogos, trilhas sonoras, softwares, ferramentas, vídeos e outros conteúdos, tratando entradas DLC legadas como `Other` para manter o comportamento visível do AppCore.
- Reforcei a cobertura da UI macOS para garantir que cada checkbox de tipo de conteúdo esteja ligado à categoria correta antes do sorteio.
- Reforcei também a cobertura do checkbox de loja Steam no macOS para garantir que ele ative o filtro de storefront e altere a lista de lojas incluídas como no ViewModel compartilhado.
- Isolei a persistência macOS de preferências para permitir testes sem tocar nas preferências reais do usuário e cobri a restauração de filtros de conteúdo, idioma e histórico recente entre sessões.
- Ampliei a cobertura de descoberta macOS para garantir que `appinfo.vdf` classifique todos os tipos visíveis de conteúdo antes dos filtros agirem: soundtrack, software, tool, video e other.
- Adicionei um smoke test de renderização SwiftUI para montar o `ContentView` em `NSHostingView` no tamanho mínimo de janela e verificar bitmap não vazio.
- Normalizei também no domínio compartilhado as categorias legadas `DLC` como `Other`, mantendo o filtro visível "Outros conteúdos" consistente entre Windows, Linux e macOS mesmo com settings ou providers antigos.
- Ajustei o macOS para preservar e re-localizar o status de jogo sorteado ao alternar BR/US, respeitar a exclusão de sorteios recentes e mostrar Play desabilitado como botão secundário.
- Re-localizei também os comandos de menu do app macOS ao alternar BR/US, mantendo `Atualizar biblioteca`/`Sortear` sincronizados com a tela SwiftUI mesmo após trocar para bootstrap AppKit explícito.
- Fixei os itens de menu macOS de `Atualizar biblioteca` e `Sortear` no delegate do app, garantindo que atalhos e menu disparem os mesmos comandos validados por `canRefresh`/`canDraw`.
- Completei no macOS as chaves de localização compartilhadas relevantes do AppCore para labels de loja/arte/metadados, contagem comum, storefront desconhecida e mensagens de launch/install.
- Adicionei um teste macOS que lê as chaves do `LocalizationService` compartilhado e garante que todas resolvem em BR/US no Swift, evitando regressões de paridade textual entre clientes.
- Alinhei literalmente as mensagens macOS compartilhadas de falha de Play/Install às frases do AppCore em português e inglês, evitando divergência visível entre Windows/Linux e macOS.
- Normalizei preferências persistidas no macOS, incluindo categorias legadas, coleção com espaços, storefronts desconhecidas e limites negativos de histórico/exclusão recente.
- Tornei a decodificação de preferências macOS tolerante a JSON parcial/legado, aplicando defaults por campo ausente antes da normalização, como o `SelectionEngine` faz ao carregar settings antigos; preferências antigas sem storefronts persistidas agora continuam sem filtro de loja enquanto `FilterByStorefront` estiver desligado.
- Alinhei o refresh macOS ao ViewModel compartilhado: se uma coleção persistida não existe mais na biblioteca carregada, o filtro é limpo para evitar uma tela presa em zero resultados.
- Alinhei também o caminho de falha do refresh macOS ao ViewModel compartilhado: quando a biblioteca não carrega, a coleção selecionada é limpa junto com a lista vazia, mantendo a mensagem de erro como status final.
- Alinhei também a seleção de coleção persistida no macOS ao ViewModel compartilhado: coleções salvas aparecem nas opções antes do refresh e são normalizadas para o nome/capitalização real retornado pela Steam.
- Alinhei a lista de coleções macOS ao ViewModel compartilhado deduplicando nomes sem diferenciar maiúsculas/minúsculas e removendo espaços extras antes de renderizar o seletor.
- Alinhei o sorteio macOS ao AppCore: candidatos agora são recalculados após a animação, usando filtros alterados durante o atraso, e requisições duplicadas enquanto um sorteio está em andamento são ignoradas.
- Alinhei também o refresh macOS ao comportamento dos comandos assíncronos do AppCore: o botão e o menu de atualizar ficam indisponíveis durante o carregamento e requisições duplicadas são ignoradas.
- Alinhei o bloqueio de sorteio durante refresh no macOS ao AppCore: a biblioteca antiga é limpa no início do carregamento, `Draw` fica indisponível e chamadas de sorteio são ignoradas até a nova biblioteca terminar de carregar.
- Alinhei o sorteio seedado macOS ao `Random(seed)` do .NET, incluindo persistência de `randomPosition`, retomada determinística entre sessões e reset da posição quando a seed muda.
- Alinhei o histórico de sorteios macOS ao `SelectionEngine`: mudanças no `historyLimit` agora apararem o histórico imediatamente, fazendo a exclusão de jogos recentes refletir a nova configuração sem esperar o próximo sorteio.
- Alinhei também o carregamento inicial do histórico macOS ao `SelectionEngine`: histórico persistido acima de `historyLimit` é aparado já no bootstrap do `AppStore`, antes de calcular elegibilidade.
- Cobri no macOS a disponibilidade do botão `Draw` após o histórico recente excluir o único candidato restante, garantindo que a UI reflita candidatos atuais depois do sorteio.
- Adicionei o contrato de checagem de updates no bootstrap macOS com implementação no-op injetável, mantendo a jornada de startup alinhada sem introduzir download automático antes de existir release macOS.
- Adicionei diagnóstico local no macOS via unified logging para refresh da biblioteca, falhas de descoberta e ações/falhas de `steam://run`/`steam://install`, mantendo a jornada de observabilidade equivalente às outras plataformas.
- Corrigi a pluralização das mensagens de disponibilidade em português no AppCore e no macOS, evitando frases como `1 jogo disponíveis` nas versões desktop.
- Adicionei notificação macOS best-effort para jogo sorteado, alinhada ao comportamento opcional de Windows/Linux sem interromper o usuário com prompt de permissão durante o sorteio.
- Adicionei testes Swift para parser VDF, appinfo, fallback de nomes, leitura de biblioteca, coleções dinâmicas, filtros de seleção, refresh, idioma, histórico recente, notificação de sorteio e comandos `steam://`, além de checklist macOS em `docs/testing/macos-port-checklist.md`.

- Redesenhei a tela principal Windows e Linux para aproximar o app do mock escuro de referencia: header com marca/idiomas, sidebar de filtros, card principal com arte ampla, badges de loja, acoes e status unificados.
- Extrai paleta, tipografia, controles, icones e bandeiras do WPF para dicionarios de tema e espelhei os recursos equivalentes no Avalonia, mantendo chips de metadata retangulares e estados desabilitados legiveis.

- Endureci o auto-update Linux exigindo SHA-256 obrigatório e válido no feed, além de restringir URLs de feed/download para HTTPS (com exceção de loopback para testes locais), bloqueando staging de binários sem integridade mínima.

- Adicionei CI/release Linux com build/test/publish do projeto `SteamBacklogPicker.Linux`, geração de pacote `.AppImage` compatível, publicação do feed JSON de update e validação automatizada de checksum + atualização pendente em ambiente Linux.
- Atualizei README e runbook de instalação para documentar o fluxo de release Linux com verificação de integridade via SHA-256 e uso do feed `linux-appimage-update.json`.

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

- Desativei a execução automática de `CheckForUpdatesAsync` no startup da aplicação WPF para impedir aplicação silenciosa de releases remotos sem âncora de confiança independente.
