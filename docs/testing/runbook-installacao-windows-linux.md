# Runbook manual – Instalação e inicialização (Windows/Linux)

Objetivo: validar que a instalação e o primeiro lançamento entregam comportamento equivalente, mudando apenas o método de instalação por plataforma.

## Matriz de execução

| Etapa | Windows | Linux | Critério de aprovação |
| --- | --- | --- | --- |
| Preparação | Confirmar Steam instalado e biblioteca com ao menos 10 jogos | Confirmar Steam instalado e biblioteca com ao menos 10 jogos | Pré-condições equivalentes |
| Instalação | Instalar pacote Windows (Squirrel/MSIX conforme release) | Instalar/rodar cliente Linux conforme release | App inicia sem erro |
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
