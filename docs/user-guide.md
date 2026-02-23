# SteamBacklogPicker – Guia do Usuário

## Primeiros passos

1. Instale o aplicativo conforme sua plataforma (detalhes em [README](../README.md)).
2. Abra o SteamBacklogPicker.
3. No primeiro uso, permita a leitura da biblioteca Steam local e escolha se deseja ativar telemetria anônima.

> Experiência de uso (UX), fluxos de filtros e sorteio são os mesmos em Windows e Linux. A diferença entre plataformas fica restrita ao método de instalação/empacotamento.

## Navegando na interface

- **Visão da biblioteca**: mostra total de jogos detectados, filtros ativos e ações rápidas de atualização.
- **Cartão do jogo sorteado**: exibe capa, status de instalação e ações disponíveis para o jogo atual.
- **Painel de filtros**: permite priorizar jogos instalados, aplicar recortes por tags/coleções e ajustar critérios de sorteio.
- **Ações principais**:
  - `Sortear novamente`: executa novo sorteio com os filtros atuais.
  - `Abrir no Steam`: abre o jogo selecionado no cliente Steam.

## Privacidade, telemetria e logs

- A telemetria é opcional e pode ser ativada/desativada em **Configurações > Privacidade**.
- Logs locais registram falhas de descoberta, inicialização e execução para suporte técnico.

## Atualização do aplicativo

- O comportamento funcional do app não muda por plataforma; apenas o mecanismo de entrega pode variar por pacote.
- Consulte o [README](../README.md) para o fluxo de instalação/execução vigente em Windows e Linux.

## Solução de problemas

- Se a biblioteca não for encontrada, revise os caminhos Steam em **Configurações** e confirme acesso à pasta `steamapps`.
- Em caso de erro recorrente, exporte/colete logs e informe a versão do app no relato.
- Se o Steam estiver fechado ou sem metadados recentes, o app pode usar cache local até nova atualização da biblioteca.

## Uso offline

- O sorteio funciona com dados locais já sincronizados.
- Filtros, histórico e preferências continuam disponíveis sem internet.

## Suporte

Para bugs ou sugestões, abra uma issue no repositório com versão do app, plataforma (Windows/Linux) e evidências de log.
