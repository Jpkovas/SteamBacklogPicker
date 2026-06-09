# Runbook manual – Instalação e inicialização (Windows/Linux)

Objetivo: validar que a instalação e o primeiro lançamento entregam comportamento equivalente, mudando apenas o método de instalação por plataforma.

## Matriz de execução

| Etapa | Windows | Linux | Critério de aprovação |
| --- | --- | --- | --- |
| Preparação | Confirmar Steam instalado e biblioteca com ao menos 10 jogos | Confirmar Steam instalado e biblioteca com ao menos 10 jogos | Pré-condições equivalentes |
| Instalação | Executar a saída Windows publicada pela release atual | Baixar e executar pacote Linux da release (`SteamBacklogPicker-<versao>-linux-x64.AppImage`) | App inicia sem erro |
| Primeiro lançamento | Abrir app pelo atalho/menu | Abrir app pelo menu/comando do ambiente gráfico | Tela principal renderiza |
| Descoberta inicial | Aguardar varredura inicial da biblioteca | Aguardar varredura inicial da biblioteca | Jogos aparecem sem inconsistência crítica |
| Telemetria | Revisar prompt de consentimento | Revisar prompt de consentimento | Mensagem e escolha equivalentes |

## Casos manuais detalhados

### Caso 1 — Instalação limpa
1. Remover versão anterior do app.
2. Instalar release atual.
3. Abrir o app e validar que não há erro de bootstrap.

**Resultado esperado:** instalação concluída, app abre e exibe UI principal.

### Nota sobre update Windows

O auto-update Squirrel legado deve permanecer desligado por padrão. Só habilite `SBP_ENABLE_LEGACY_WINDOWS_UPDATE=true` em validação controlada; o canal oficial deve aguardar assinatura Authenticode/pinning de certificado.

### Caso 2 — Primeiro scan de biblioteca
1. Com Steam aberto, iniciar o app.
2. Aguardar a primeira indexação.
3. Registrar total de jogos e comparar com biblioteca local conhecida.

**Resultado esperado:** total detectado compatível com a conta/biblioteca em cada plataforma.

### Caso 3 — Prompt de consentimento
1. No primeiro uso, registrar o texto e opções do consentimento de telemetria.
2. Escolher uma opção.
3. Reiniciar o app.

**Resultado esperado:** escolha persistida e UX equivalente entre plataformas.

## Evidências obrigatórias

- Captura da tela inicial após instalação.
- Total de jogos detectados no primeiro scan.
- Registro da decisão de telemetria.


## Referência de descoberta Steam no Linux

Para diagnóstico de ambiente Linux, a resolução de caminho do Steam usa esta ordem: `STEAM_PATH` válido, `XDG_DATA_HOME/Steam`, caminhos tradicionais (`~/.steam/steam`, `~/.steam/debian-installation`, `~/.local/share/Steam`) e depois Flatpak/Snap. Cada caminho só é aceito se contiver `steamapps/libraryfolders.vdf`.


## Fluxo de release Linux (validação manual)

1. Baixar `SteamBacklogPicker-<versao>-linux-x64.AppImage` e `linux-appimage-update.json` da mesma release.
2. Validar checksum com `sha256sum` e comparar com o campo `sha256` do feed JSON.
3. Executar o pacote com permissão de execução (`chmod +x`).
4. Validar feed assinado: quando o segredo `SBP_LINUX_UPDATE_PRIVATE_KEY` estiver configurado no workflow, confirmar que o feed contém `signature` e que o app recebe a chave pública correspondente em `SBP_LINUX_UPDATE_PUBLIC_KEY`.
5. Validar update pendente: publicar nova release apontada no feed, reiniciar o app e confirmar substituição do binário no próximo start.

**Resultado esperado:** pacote íntegro, feed compatível (`version`, `downloadUrl`, `sha256`, `signature` quando assinado) e atualização aplicada sem erro no ciclo de reinicialização. Feed sem assinatura deve exigir `SBP_ENABLE_UNSIGNED_LINUX_UPDATE_FEED=true` e ficar restrito a teste local.


## Validação Linux de `/proc/<pid>/mem`

Objetivo: confirmar em Linux real que o modo seguro continua sendo fallback/no-op e que leitura de memória só ocorre quando `EnableUnsafeLinuxMemoryRead` é habilitado em ambiente controlado.

1. Em uma instalação Linux normal, sem privilégios extras, executar a suíte `SteamHooks.Tests`.
2. Confirmar que `Create_MemoryModeWithoutLinuxFlag_ShouldDegradeWhenMemoryModeIsUnsupported` emite `steam_hook_memory_mode_degraded` e usa cliente no-op.
3. Em ambiente de laboratório, habilitar `EnableUnsafeLinuxMemoryRead` e registrar o resultado com `ptrace_scope` padrão.
4. Repetir com política endurecida (`ptrace_scope` restritivo/AppArmor ativo) e confirmar diagnóstico `steam_hook_linux_memory_read_denied` ou `steam_hook_memory_reader_unavailable`.
5. Não promover leitura de `/proc/<pid>/mem` para padrão de produto; manter o fluxo como opt-in experimental.

**Resultado esperado:** a configuração padrão nunca abre `/proc/<pid>/mem`; ambientes bloqueados degradam sem falhar; qualquer leitura real fica documentada como exceção experimental.
