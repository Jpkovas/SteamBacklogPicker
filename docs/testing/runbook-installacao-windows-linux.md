# Runbook manual – Instalação e inicialização (Windows/Linux)

Objetivo: validar que a instalação e o primeiro lançamento entregam comportamento equivalente, mudando apenas o método de instalação por plataforma.

## Matriz de execução

| Etapa | Windows | Linux | Critério de aprovação |
| --- | --- | --- | --- |
| Preparação | Confirmar Steam instalado e biblioteca com ao menos 10 jogos | Confirmar Steam instalado e biblioteca com ao menos 10 jogos | Pré-condições equivalentes |
| Instalação | Instalar pacote Windows (Squirrel/MSIX conforme release) | Baixar e executar pacote Linux da release (`SteamBacklogPicker-<versao>-linux-x64.AppImage`) | App inicia sem erro |
| Primeiro lançamento | Abrir app pelo atalho/menu | Abrir app pelo menu/comando do ambiente gráfico | Tela principal renderiza |
| Descoberta inicial | Aguardar varredura inicial da biblioteca | Aguardar varredura inicial da biblioteca | Jogos aparecem sem inconsistência crítica |
| Telemetria | Revisar prompt de consentimento | Revisar prompt de consentimento | Mensagem e escolha equivalentes |

## Casos manuais detalhados

### Caso 1 — Instalação limpa
1. Remover versão anterior do app.
2. Instalar release atual.
3. Abrir o app e validar que não há erro de bootstrap.

**Resultado esperado:** instalação concluída, app abre e exibe UI principal.

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

Para diagnóstico de ambiente Linux, a resolução de caminho do Steam usa esta ordem: `STEAM_PATH` válido, caminhos tradicionais (`~/.steam/steam`, `~/.steam/debian-installation`, `~/.local/share/Steam`) e depois Flatpak/Snap. Cada caminho só é aceito se contiver `steamapps/libraryfolders.vdf`.


## Fluxo de release Linux (validação manual)

1. Baixar `SteamBacklogPicker-<versao>-linux-x64.AppImage` e `linux-appimage-update.json` da mesma release.
2. Validar checksum com `sha256sum` e comparar com o campo `sha256` do feed JSON.
3. Executar o pacote com permissão de execução (`chmod +x`).
4. Validar update pendente: publicar nova release apontada no feed, reiniciar o app e confirmar substituição do binário no próximo start.

**Resultado esperado:** pacote íntegro, feed compatível (`version`, `downloadUrl`, `sha256`) e atualização aplicada sem erro no ciclo de reinicialização.
