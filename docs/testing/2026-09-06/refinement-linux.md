# Refinamento da interface Avalonia — 6 de setembro de 2026

## Alterações verificadas

- Sidebar com marca e quatro destinos; removidos o cartão Steam Family, seu contador e a frase redundante nessa área.
- Busca flexível, controle Instalados e filtros extras na primeira linha. Origem usa três segmentos de largura igual; o estado do backlog continua acessível na segunda linha.
- Filtros extras ocupam espaço no layout, em duas colunas, com coleção e compatibilidade à esquerda e tipos de conteúdo à direita. O badge usa `AdvancedFilterCount`/`HasAdvancedFilterCount` do viewmodel compartilhado. Limpeza e Escape continuam funcionando.
- Botões e campos regulares com 40 px, ações principais com 44 px, raios de 8–12 px. Cartões de sugestões preenchem suas colunas e mantêm o mesmo topo e altura, inclusive quando o texto varia.
- O hero apresenta apenas a ação Play ou Install correspondente ao jogo. Um botão com `SelectedBacklogLabel` abre as seis ações existentes do backlog; o comando e a opção de desfazer foram preservados.
- Feedback de opacidade de 180 ms ao interagir, sem animações de layout, loops ou observadores periódicos. O efeito de transformação padrão do tema Fluent foi removido.

## Movimento reduzido

`SBP_REDUCED_MOTION=1` (ou `true`) desativa as transições. Em Linux com `/usr/bin/gsettings`, a janela consulta uma vez `org.gnome.desktop.interface enable-animations`, com timeout de um segundo, execução sem shell e cancelamento ao fechar. O schema oficial do GNOME documenta essa preferência como a chave de animações da interface: [schema do GNOME](https://github.com/GNOME/gsettings-desktop-schemas/blob/master/schemas/org.gnome.desktop.interface.gschema.xml.in).

Avalonia 11.3.17 não oferece essa preferência na API pública `IPlatformSettings` instalada. Em outros desktops, a variável acima fornece o controle explícito; mudanças da preferência do GNOME durante uma sessão serão lidas na próxima abertura. O teste headless verifica a transição finita e a remoção das transições quando a classe de movimento é desativada. A integração real com GNOME não foi executada neste host Windows.

## Testes e desempenho

Executado com o SDK .NET 8 isolado em `artifacts/audit/runtime/dotnet`, CLI home e cache NuGet isolados, sem rede nem acesso à biblioteca Steam real:

```powershell
& "$env:DOTNET_ROOT/dotnet.exe" test tests/Presentation/SteamBacklogPicker.Linux.Tests/SteamBacklogPicker.Linux.Tests.csproj --no-restore -c Release --logger 'trx;LogFileName=refinement-linux.trx' --results-directory artifacts/audit/TestResults
```

Resultado final: **24 aprovados, 11 ignorados, 0 falhas**, em 35 testes. Os 11 ignorados usam `LinuxFact` e cobrem a aplicação nativa de atualizações Linux; serão executados no job Ubuntu. [TRX local](../../../artifacts/audit/TestResults/refinement-linux.trx).

Os testes Avalonia headless usam controles e renderização Skia reais. Cobrem navegação, Ctrl+F, Escape, instalação, desenho, histórico, seleção de coleção após snapshots e mudança de idioma, origem, badge, layout dos filtros a 980×680, menu de backlog e desfazer. A visibilidade de Play/Install e a igualdade de dimensões dos segmentos/cartões são verificadas por propriedades dos controles, sem comparação frágil de strings XAML.

Uma biblioteca de 5.000 jogos manteve entre 1 e 30 containers realizados, menos de 40 consultas de arte no primeiro viewport e menos de 80 após rolar até o último jogo. Isso verifica a virtualização e a descoberta de capas sob demanda; não representa um benchmark de GPU ou tempo de resposta nativo.

O código de QR reaproveita o bitmap quando o desafio não muda, evitando recodificação por atualizações de sincronização. O teste confirma identidade do bitmap e sua remoção ao encerrar o desafio. A arte continua carregada de modo assíncrono, com cancelamento e liberação de bitmap ao remover um controle da árvore visual.

## Evidência visual

As capturas usam fixtures de dois jogos sem capas externas. A exportação é opcional e só ocorre quando `SBP_UI_EVIDENCE_DIR` é informado.

- [Antes, descoberta](../../../artifacts/audit/refinement-linux-before/discover.png) e [filtros anteriores](../../../artifacts/audit/refinement-linux-before/filters.png).
- [Depois, descoberta](../../../artifacts/audit/refinement-linux-after/discover.png), [filtros na largura mínima](../../../artifacts/audit/refinement-linux-after/filters-minimum.png) e [menu do backlog](../../../artifacts/audit/refinement-linux-after/backlog-menu.png).

## Revisão de integração e limites

O workflow continua restaurando explicitamente a suíte Avalonia e o RID antes do publish Linux. O relatório de paridade agora classifica `SteamCatalog.Tests` em Integration. A inspeção de `Package.swift`, `Package.resolved`, do job macOS e do script de distribuição confirmou que a dependência zstd permanece fixada e que o pacote de desenvolvimento informa assinatura ad hoc sem prometer notarização. Não houve redesenho nem alteração Swift nesta rodada.

O host Windows validou compilação .NET, controles Avalonia headless e capturas; não executou compositor Linux, GNOME, Swift/AppKit ou os jobs remotos. `git diff --check` passou nos arquivos Avalonia, testes e workflow, com apenas avisos de conversão LF/CRLF do Git.
