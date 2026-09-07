# Auditoria do Steam Backlog Picker

Data: 6 de setembro de 2026. Repositório: https://github.com/Jpkovas/SteamBacklogPicker. Revisão: c45eb915f63b0855988eca9d9c1bcb01dc42b86e.

## Parecer

O programa tem uma base aproveitável: separação entre descoberta, seleção e apresentação; composição por interfaces; persistência atômica de preferências; parsers com suporte a formatos modernos no .NET; e testes automatizados. Eu manteria esses investimentos.

O principal problema atual é a confiança na biblioteca. O programa mistura registros conhecidos pelo Steam, propriedade, compartilhamento e instalação. Isso produz uma interface que pode mostrar uma capa correta com um título desconhecido e indicar como instalado algo encontrado apenas no histórico/cache. O redesenho deve nascer sobre um modelo de dados corrigido.

Prioridade recomendada: **biblioteca correta → sincronização e Steam Family → descoberta e UI → distribuição e paridade entre plataformas**. O desenho visual pode ser explorado em paralelo às correções, usando estados de dados explicitamente definidos.

## O que foi verificado

- Clone e revisão dos 217 arquivos rastreados, incluindo produção, testes, fixtures, scripts, workflows e documentação; revisão independente de segurança e arquitetura.
- Restore e build Release da solução em Windows: sucesso, zero avisos e zero erros.
- 154 execuções de testes da solução aprovadas; mais 25 da suíte Linux executada separadamente no Windows. São 179 resultados, com repetição entre frameworks; não são 179 testes exclusivos.
- Testes do atualizador Linux retornam antecipadamente fora do Linux. Esses resultados não validam instalação/assinatura/troca de binário em Linux.
- Consulta NuGet de vulnerabilidades, incluindo dependências transitivas da solução: nenhum aviso retornado nesta execução.
- Aplicação WPF aberta de verdade, com biblioteca Steam local, sorteio e filtros operados e quatro capturas verificadas.
- Probes usando os serviços de produção e fixtures sintéticas para ausência de Installed, compartilhamento e troca de conta.
- Pesquisa das interfaces Steamworks, SteamKit e mensagens Steam Families nas fontes relacionadas na proposta técnica.
- Fontes de produção preservadas. Foram acrescentados documentos, capturas e artefatos de auditoria. Nenhum jogo foi iniciado ou instalado.

Limites: sem execução nativa Linux/macOS, sem validação autenticada do serviço Steam Families, sem comparação completa com todas as licenças da conta, sem auditoria integral com leitor de tela, múltiplos DPIs ou controle. O resultado de segurança é uma revisão Standard de passagem única, não uma garantia de ausência de falhas.

## Evidência da biblioteca local

Resultados de duas leituras consecutivas foram consistentes nas contagens. Última leitura: 1.520 ms; anterior: 1.573 ms. São medições pontuais, não benchmark estatístico.

| Medida | Resultado |
| --- | ---: |
| Entradas retornadas pelo serviço | 451 |
| Títulos ainda no formato “App ID” | 100, ou 22,2% |
| Marcadas como instaladas | 444 |
| Manifests encontrados nas bibliotecas detectadas | 14 |
| Marcadas instaladas sem manifest correspondente | 430 |
| Classificadas como compartilhadas | 0 |
| Operação completou sincronamente | Sim |
| Registros lidos do appinfo local | 3.073 |
| Versão do appinfo / tamanho | 41 / 9.375.507 bytes |

Manifests são evidência local de instalação, não um inventário autoritativo de licenças. A contagem zero de compartilhados também não prova que a conta não tenha família. O dado relevante é a discrepância reproduzida e a ausência de uma fonte completa de acesso familiar.

O parser .NET conseguiu ler o appinfo local sem erro. Portanto, neste ambiente, atribuir os nomes ausentes apenas a uma “versão nova do VDF” seria incorreto.

[Resultados reproduzíveis e agregados](<evidence/probe-results.json>).

## Achados priorizados

P1 = compromete a função central ou a entrega; P2 = degrada experiência, confiabilidade ou manutenção; P3 = melhoria posterior. Essas são prioridades de produto/engenharia, não severidades de vulnerabilidade.

### P1 · F01 — Ausência de Installed vira instalado

Em [SteamVdfFallback.cs](https://github.com/Jpkovas/SteamBacklogPicker/blob/c45eb915f63b0855988eca9d9c1bcb01dc42b86e/src/Integration/SteamClientAdapter/SteamVdfFallback.cs#L209), o fallback usa isInstalled ?? true. Uma entrada contendo somente LastPlayed já recebe IsInstalled=true. A fixture confirmou isso sem qualquer manifest.

Impacto: filtro “Apenas instalados”, contagens e ação Jogar deixam de representar a instalação real. Esse caminho explica os 430 registros marcados como instalados sem manifest na leitura local.

Correção: ausência de evidência deve produzir estado desconhecido; instalação confirmada deve vir de manifest válido/estado do cliente. Separar descoberta de ID, metadados, acesso e instalação, com precedência explícita entre fontes.

Aceite: um registro só com LastPlayed nunca conta como instalado; bibliotecas adicionais e volumes desconectados têm tratamento definido; dados desconhecidos não viram permissões positivas.

### P1 · F02 — Compartilhado ocupa o lugar de instalado

[SteamLibraryProvider.cs](https://github.com/Jpkovas/SteamBacklogPicker/blob/c45eb915f63b0855988eca9d9c1bcb01dc42b86e/src/Presentation/SteamBacklogPicker.AppCore/Services/Library/SteamLibraryProvider.cs#L96) substitui instalação por Shared. [SelectionEngine.cs](https://github.com/Jpkovas/SteamBacklogPicker/blob/c45eb915f63b0855988eca9d9c1bcb01dc42b86e/src/Domain/SelectionEngine/SelectionEngine.cs#L254) aceita Shared no filtro de instalados, enquanto [GameLaunchService.cs](https://github.com/Jpkovas/SteamBacklogPicker/blob/c45eb915f63b0855988eca9d9c1bcb01dc42b86e/src/Presentation/SteamBacklogPicker.AppCore/Services/Launch/GameLaunchService.cs#L46) só habilita Jogar para Installed.

Reprodução: o mesmo jogo compartilhado passa no filtro de instalados, tem Jogar indisponível e Instalar disponível. Um jogo familiar pode estar instalado ou não; essas informações precisam coexistir. Há lógica equivalente no cliente Swift.

Correção: Ownership/Access e Installation como dimensões independentes. Disponibilidade de cópias da família deve ser uma terceira informação, potencialmente desconhecida.

Aceite: matriz próprio/familiar × instalado/não instalado/desconhecido, cobrindo filtro, rótulo e ação, em todos os clientes.

### P1 · F03 — Integração nativa não enumera a biblioteca familiar

O adapter resolve exports sem versão e declara BIsSubscribedFromFamilySharing com appId: [SteamClientAdapter.cs](https://github.com/Jpkovas/SteamBacklogPicker/blob/c45eb915f63b0855988eca9d9c1bcb01dc42b86e/src/Integration/SteamClientAdapter/SteamClientAdapter.cs#L239). A função documentada não recebe um appId arbitrário: informa o compartilhamento do aplicativo corrente. Corrigir apenas a assinatura não cria uma listagem da família. [Documentação ISteamApps](https://partner.steamgames.com/doc/api/ISteamApps).

A inicialização Steamworks também exige contexto válido de aplicativo; procurar DLLs no diretório Steam não estabelece esse contexto. Os mocks cobrem a implementação simulada, não comprovam compatibilidade com o SDK real. [Inicialização Steam API](https://partner.steamgames.com/doc/api/steam_api).

Correção: tratar o adapter como opcional e explicitamente limitado; validar ABI contra a versão escolhida caso seja mantido. Fazer um protótipo separado e autenticado de FamilyGroups com SteamKit, preservando o modo local.

### P1 · F04 — CI Linux pode falhar antes dos testes

O projeto SteamBacklogPicker.Linux.Tests não está na solução. Os dois workflows restauram apenas a solução e depois executam esse projeto com --no-restore: [CI](<../../../.github/workflows/dotnet.yml>) e [release Linux](<../../../.github/workflows/linux-release.yml>).

A reprodução, retirando temporariamente apenas o project.assets.json gerado e restaurando-o em finally, produziu NETSDK1004. A suíte passa quando seu próprio restore é executado. O problema é a preparação de um checkout limpo.

Correção: incluir a suíte na solução ou restaurá-la explicitamente. Verificar também os assets do runtime específico usados em publish --no-restore. Executar a matriz em runners nativos e marcar condições não exercitadas como skip real, sem return silencioso.

[Log da reprodução](<evidence/ci-clean-assets-probe.log>).

### P2 · F05 — Cache sobrevive à mudança de conta ou arquivo

[SteamVdfFallback.cs](https://github.com/Jpkovas/SteamBacklogPicker/blob/c45eb915f63b0855988eca9d9c1bcb01dc42b86e/src/Integration/SteamClientAdapter/SteamVdfFallback.cs#L35) guarda compartilhamento somente por appId; o appinfo deixa de ser recarregado após sucesso em _appInfoLoaded. Na fixture, trocar loginusers para outra conta sem a flag de compartilhamento manteve true para o mesmo jogo.

Correção: escopo por conta, geração de sincronização e invalidação por mudanças do arquivo. Troca de conta deve cancelar trabalhos antigos; resultados atrasados não podem entrar na conta nova. Capturar um snapshot por refresh evita misturar leituras de momentos diferentes.

### P2 · F06 — Refresh bloqueia e pode descartar a última biblioteca boa

[SteamLibraryProvider.cs](https://github.com/Jpkovas/SteamBacklogPicker/blob/c45eb915f63b0855988eca9d9c1bcb01dc42b86e/src/Presentation/SteamBacklogPicker.AppCore/Services/Library/SteamLibraryProvider.cs#L177) retorna Task.FromResult depois do trabalho síncrono. O caminho observado levou cerca de 1,5 segundo antes de retornar a Task. A assinatura Async não move esse trabalho para outra thread.

[MainViewModel.cs](https://github.com/Jpkovas/SteamBacklogPicker/blob/c45eb915f63b0855988eca9d9c1bcb01dc42b86e/src/Presentation/SteamBacklogPicker.AppCore/ViewModels/MainViewModel.cs#L119) limpa a biblioteca antes de concluir a nova leitura; falhas removem o estado anterior e afetam a coleção selecionada. Watchers internos não publicam atualizações incrementais para o snapshot do ViewModel.

Correção: coordenador de sincronização em segundo plano, cancelamento, deduplicação, eventos agregados com debounce e troca atômica de snapshots. Manter os dados anteriores visíveis com estado “atualizando” ou “atualização falhou”.

### P2 · F07 — Metadados incompletos e estratégias divergentes

O fallback .NET extrai IDs de arquivos librarycache, mas não aproveita o conteúdo JSON como o cliente Mac faz. O resolvedor remoto de nomes existe no Mac, não no fluxo Windows observado. Na tela real, a capa do Black Ops II apareceu com o título “App 202990”.

No Mac, [SteamAppNameResolver.swift](https://github.com/Jpkovas/SteamBacklogPicker/blob/c45eb915f63b0855988eca9d9c1bcb01dc42b86e/src/Presentation/SteamBacklogPicker.Mac/Services/SteamAppNameResolver.swift#L99) agenda uma tarefa por nome ausente e espera até 25 segundos; falta uma fila explícita limitada, política de 429/backoff, invalidação por idioma e expiração. Callbacks podem continuar depois do timeout.

Correção: enriquecimento comum por appId/idioma, cache persistente próprio e atualização incremental. Priorizar títulos visíveis e os candidatos do sorteio. Respeitar indisponibilidade sem apresentar um identificador como título definitivo.

### P2 · F08 — Modernizar a UI exige mudar a hierarquia

A superfície principal é dominada por opções técnicas de filtragem, enquanto o benefício — escolher algo interessante para jogar — só surge após o sorteio. Falta visão navegável da biblioteca, busca e explicação do que foi descoberto.

Há uma boa base de tokens de cor, tipografia e componentes. Reaproveitar essa estrutura e redesenhar a composição, estados e navegação. A seção visual abaixo registra o que foi realmente observado.

### P2 · F09 — Acessibilidade tem lacunas concretas

TextMuted #5C6B80 sobre Surface #11151D resulta em aproximadamente 3,37:1. Os estilos usam essa cor para rótulos de 12px. É inferior à referência de 4,5:1 para texto normal; usar WCAG como critério de projeto, sem declarar conformidade de desktop a partir disso. [Tokens](https://github.com/Jpkovas/SteamBacklogPicker/blob/c45eb915f63b0855988eca9d9c1bcb01dc42b86e/src/Presentation/SteamBacklogPicker.UI/Themes/Palette.xaml#L18), [tipografia](https://github.com/Jpkovas/SteamBacklogPicker/blob/c45eb915f63b0855988eca9d9c1bcb01dc42b86e/src/Presentation/SteamBacklogPicker.UI/Themes/Typography.xaml#L27), [W3C](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html).

O título selecionado tem AutomationProperties.Name genérico (“Título do jogo selecionado”), observado também na árvore acessível. O nome acessível deve comunicar o título efetivo, com descrição complementar separada.

Um movimento de Tab foi verificado e funcionou. Ainda faltam percorrer a jornada inteira por teclado/leitor de tela, DPI alto, aumento de fonte, foco após sorteio e preferências de movimento reduzido.

### P2 · F10 — Três apresentações, dois motores de produto

Windows WPF e Linux Avalonia compartilham AppCore/.NET; Mac reimplementa domínio, seleção, descoberta, localização e cache em Swift. Divergências já existem: resolvedor de nomes só no Mac, tratamento diferente de JSON e payload comprimido VSZa ignorado pelo parser Swift.

Recomendação: corrigir contratos e criar fixtures de paridade antes de mudar framework. Para o redesenho inicial, WPF pode entregar uma UI moderna. Avaliar unificar Windows/Linux em Avalonia com uma fatia funcional de teste antes de assumir o custo. Não iniciar uma reescrita total somente para mudar a aparência.

### P2 · F11 — Testes verdes ainda deixam passar as falhas centrais

Mocks de API nativa não validam assinaturas reais. Parte dos testes de UI verifica XAML/strings e não a experiência renderizada. Fixtures não cobrem adequadamente conta trocada, jogos conhecidos sem Installed, estado familiar instalado, revogação, formatos truncados e respostas parciais.

Adicionar testes de comportamento para F01–F06, fixtures modernas sanitizadas e poucos testes de jornada realmente renderizados. Manter testes de baixo custo onde úteis, mas não usar presença de texto em XAML como prova de qualidade visual.

### P2 · F12 — Robustez de parsers e serviços opcionais

Parsers precisam de limites de tamanho, profundidade, contagem de strings e descompressão; leitores Swift precisam checar comprimentos antes de slices. Hoje os arquivos são caches locais Steam. A revisão não estabeleceu um atacante externo controlando esses bytes, portanto isso é tratado como robustez/hardening, não como vulnerabilidade remota confirmada.

A primeira abertura sob sandbox falhou ao criar a pasta do sink de telemetria, mesmo com eventos desabilitados. A abertura normal permitida funcionou. O caso evidencia que um serviço opcional não deveria impedir startup diante de acesso negado ou disco indisponível.

### P2 · F13 — Distribuição e atualização precisam virar um fluxo verificável

O updater Linux verifica assinatura e hash, mas o produto precisa provisionar a chave pública e validar toda a entrega em Linux. Revalidar o arquivo pendente no momento da aplicação fortaleceria o desenho. O updater Windows legado é registrado, mas não é chamado no startup inspecionado; Mac usa no-op.

Faltam demonstrações atuais de instalação, upgrade, rollback, assinatura e experiência de primeira execução por plataforma. Fixar a versão/digest do appimagetool hoje obtido pelo canal continuous reduz variação da cadeia de build.

Planejar migração de .NET 8, cujo suporte termina em 10/11/2026, para .NET 10 LTS após validar dependências e plataformas. [Política Microsoft](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

### P3 · F14 — Descoberta hoje é predominantemente sorteio uniforme

[SelectionEngine.cs](https://github.com/Jpkovas/SteamBacklogPicker/blob/c45eb915f63b0855988eca9d9c1bcb01dc42b86e/src/Domain/SelectionEngine/SelectionEngine.cs#L355) atribui peso 1 a todos. Existem histórico e exclusão de recentes, mas a exclusão padrão é zero e isso não constitui gestão de backlog.

Evolução: “Quero jogar”, “Jogando”, “Concluído”, “Não tenho interesse”, “Mais tarde”, exclusão explícita de repetidos e presets. Depois, sugestões com justificativas simples e transparentes. Dados de duração, humor ou gênero precisam de fontes confiáveis; não inventar informações ausentes.

### P3 · F15 — Documentação e nomes prometem além do implementado

Há guias/checklists com onboarding, privacidade, paridade e melhorias que não correspondem necessariamente ao runtime. Atualizar com comportamento verificado, plataforma e data. AppCore conserva namespaces UI e o domínio persiste arquivos, apesar da separação descrita na arquitetura. São bons pontos para refatorações pequenas, sem bloquear correções de produto.

## Auditoria visual da jornada Windows

Capturas reais, salvas e inspecionadas nesta execução. Viewport capturado: 1268 × 794. Não houve instalação ou lançamento de jogo.

### 1. Início — funcional, hierarquia fraca

![Tela inicial](<screenshots/01-inicio.png>)

**Funciona:** tema coerente, filtros agrupados e contagem de candidatos visível.

**Problemas:** 416 candidatos aparecem sem uma amostra navegável; “Nenhum jogo selecionado” se repete; o espaço principal fica vazio; informações de instalação são apresentadas antes de existir uma escolha; opções importantes competem com categorias de conteúdo pouco frequentes. Parte da configuração depende de rolagem na lateral.

**Mudança:** iniciar com uma seleção de jogos e botão “Escolher para mim”, com filtros comuns compactos e opções avançadas recolhidas. Mostrar conta, origem dos dados e última sincronização.

### 2. Sorteio — funciona, resultado pouco confiável

![Resultado do sorteio](<screenshots/02-sorteio.png>)

**Funciona:** capa carrega, seleção tem destaque e ações aparecem no contexto.

**Problemas:** “App 202990” apesar da capa reconhecível; informação de instalado repetida; pouco contexto para decidir se vale jogar; confiabilidade do estado comprometida por F01.

**Mudança:** nome real, imagem adequada à proporção, proprietário/origem, estado confirmado, pequenos atributos úteis e motivo da sugestão. Ações “Jogar”, “Instalar”, “Mais tarde” e “Outra opção” devem seguir estados claros.

### 3. Apenas instalados — interação funciona, semântica falha

![Filtro de instalados](<screenshots/03-filtro-instalados.png>)

**Funciona:** o contador reage à alteração.

**Problema:** 409 dos 451 registros continuam elegíveis; a medição de manifests e a fixture demonstram que o rótulo do filtro não corresponde ao que o serviço confirma.

**Mudança:** corrigir o modelo antes de redesenhar o chip “Instalados”. Não compensar dado incorreto com texto mais bonito.

### 4. Nenhum candidato — bloqueio correto, recuperação insuficiente

![Sem resultados](<screenshots/04-sem-resultados.png>)

**Funciona:** Sortear é desabilitado quando nenhum tipo de conteúdo está selecionado.

**Problemas:** a tela continua instruindo a clicar em Sortear, que está desabilitado; a indicação verde não explica o estado; não oferece recuperação direta.

**Mudança:** “Nenhum jogo corresponde a estes filtros”, mostrar filtros responsáveis e ação “Limpar filtros”. Diferenciar biblioteca vazia, sincronização incompleta, falha de leitura e zero resultados.

## Direção de produto e visual

Transformar o picker em uma biblioteca pessoal que ajuda a decidir o próximo jogo:

- **Descobrir:** um destaque e alternativas com motivos curtos; acesso imediato a “Escolher para mim”.
- **Biblioteca:** busca rápida, grade/lista virtualizada, coleções, próprios/família, instalação e estados de backlog.
- **Histórico:** sorteios e decisões, com desfazer e opção de evitar repetições.
- **Configurações:** conta/fontes, sincronização, idioma, privacidade e diagnóstico exportável.

Manter a identidade escura, melhorar contraste e reduzir bordas redundantes. Dar às capas um papel maior na navegação, usando recursos de tamanho apropriado. Reservar animações para transições breves e oferecer movimento reduzido. Evitar um painel inteiro de filtros permanentes; filtros de “instalados”, “família” e “não jogados” merecem prioridade quando os dados estiverem disponíveis.

A implementação precisa cobrir: carregamento inicial, cache offline, metadados parciais, autenticação expirada, volume removido, título/capa ausentes, zero resultados, acesso familiar removido e sincronização em andamento. A UI não deve chamar um dado de “próprio” ou “disponível agora” quando a fonte só permite “conhecido”.

## Segurança e privacidade

Não foi estabelecida uma vulnerabilidade reportável com caminho de ataque sustentado pelo código na revisão realizada. Isso não elimina os problemas funcionais nem certifica segurança.

Controles positivos: URIs de jogo construídas com IDs numéricos; nenhum título vira comando; atualizações Linux verificam assinatura/hash no recebimento; telemetria .NET vem desabilitada; persistência de preferências usa gravação atômica. A consulta NuGet não retornou avisos conhecidos na data.

Trabalho recomendado: limites nos parsers, isolamento do cache por conta, armazenamento seguro de tokens caso o login SteamKit seja introduzido, revalidação de update pendente, cadeia de build fixada e documentação correta de tráfego remoto. Capas remotas e nomes enviados a endpoints são comportamento de rede distinto da telemetria; oferecer um modo local/offline claro.

Logs não devem ser chamados de “anônimos” sem verificar campos: caminhos locais e IDs de jogos podem estar presentes. Hooks de memória/pipe são protótipos sem composição ativa; não são a direção recomendada para melhorar descoberta.

O [relatório gerado do Codex Security](<security/report.md>) e seus documentos canônicos estão na pasta security desta auditoria. A ferramenta mediu 17.939.143 tokens agregados em quatro tarefas, incluindo 16.977.664 tokens de entrada em cache; esses números representam o processamento acumulado informado pelo plugin, não o tamanho deste relatório nem uma estimativa de custo.

O fechamento registrou que a árvore de trabalho mudou durante a análise. O diff dos arquivos rastreados continuou vazio: foram acrescentados os documentos e artefatos desta auditoria. O resultado de segurança está vinculado à revisão original informada no início.

Documentos canônicos: [manifesto](<security/scan-manifest.json>), [achados](<security/findings.json>) e [cobertura](<security/coverage.json>).

## Plano de execução

| Etapa | Entrega | Critério de conclusão |
| --- | --- | --- |
| 1. Confiabilidade | Corrigir F01/F02/F05/F06 e CI; separar instalação/acesso | Matriz de estados passa; troca de conta não herda flags; refresh falho preserva snapshot; CI passa em checkout limpo |
| 2. Catálogo local | Cache persistente, leitura útil de librarycache, fila de nomes/capas, diagnóstico de fontes | Offline abre último snapshot; cancelamento/retomada funcionam; ausências têm motivo; nenhuma escrita no cache Steam |
| 3. Steam Family | Protótipo autenticado SteamKit, reconciliação de IDs e exclusões | Jogos de familiar nunca instalados aparecem; próprios não duplicam; logout/revogação/erros são tratados; comparação manual com cliente Steam |
| 4. Nova experiência | Escolher direção visual, implementar Descobrir/Biblioteca/Histórico/Configurações | Jornada completa com dados reais/parciais, teclado, DPI, contraste, vazio e erro; ações coerentes |
| 5. Descoberta melhor | Backlog, anti-repetição, presets e sugestões explicáveis | Usuário consegue excluir, adiar, concluir e entender por que um título foi sugerido |
| 6. Entrega sustentável | .NET LTS, empacotamento, atualizações, paridade e documentação | Instalação/upgrade/rollback nativos por plataforma; fixtures comuns; documentação compatível com releases |

A etapa 3 deve começar como prova de viabilidade curta antes de prometer cobertura completa de Steam Family. O redesenho pode ser explorado durante as etapas 1–3, mas sua integração depende dos estados corretos.

[Proposta detalhada de Steam Family e cache](<STEAM-FAMILY-E-CACHE.md>).

## Artefatos e reprodução

Os logs completos de restore/build/testes e o SDK isolado estão em artifacts/audit. Os probes não modificam código de produção nem caches Steam; as fixtures são criadas em artifacts/audit/probe-data. Os resultados agregados, diagnóstico NuGet, erro de CI e capturas foram copiados para esta pasta para revisão.

O SDK foi instalado somente em artifacts/audit/runtime/dotnet, sem alteração persistente do PATH. Não há commit ou publicação automática desta auditoria.
