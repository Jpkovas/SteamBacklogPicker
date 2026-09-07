# Refinamento de confiabilidade e performance — 2026-09-06

Escopo: MainViewModel/Workspace, filtros, GameDetails, persistência do SelectionEngine, ArtworkCache e controle de arte WPF. Testes Windows/.NET 8.0.424 com fixtures temporárias e HTTP simulado; nenhuma preferência, cache ou conta Steam real foi alterada por estes testes. XAML, animações e serviços de catálogo foram tratados em outras frentes.

## Correções verificadas

- Cartões reaproveitados por identificador/conteúdo; resolver arte passa a ocorrer quando o controle solicita a imagem. Filtros equivalentes e snapshots sem mudanças mantêm o ItemsSource. Alterações de backlog invalidam somente o cartão afetado; idioma/conta invalidam o conjunto apropriado.
- Contadores da biblioteca calculados no snapshot. As sugestões ordenam candidatos quando a lista muda e reutilizam os quatro melhores ao mudar a seleção.
- Histórico de até 200 entradas usa inserção/remoção incremental (duas notificações no limite), em vez de Clear + 200 Add. Substituições completas e listas de coleções emitem um Reset. Dispose remove também a inscrição de localização das preferências.
- Sorteio e gravação do histórico executam fora do dispatcher. O resultado só atualiza a seleção na mesma conta e versão dos dados/filtros que iniciaram o sorteio. Cartões antigos são resolvidos novamente na biblioteca atual; refresh tardio após Dispose não publica alterações. Um sorteio já gravado durante a troca permanece na conta que o iniciou e não aparece no histórico da nova conta.
- Undo só é disponibilizado depois de salvar o backlog. Valores indefinidos do enum são ignorados. O SelectionEngine ignora entradas nulas de histórico ao abrir e restaura preferências, histórico e posição do gerador se a gravação falhar. A UI restaura os filtros efetivamente salvos e mostra o erro de persistência.
- WPF decodifica imagens fora do dispatcher, em larguras 160/320/640/1200 conforme largura/DPI; compartilha bitmaps congelados, limita o LRU a 64 MiB/128 entradas e duas decodificações simultâneas. Unloaded libera Source; tamanho original acima de 64 milhões de pixels é rejeitado. Arte corrompida não derruba o controle.
- ArtworkCache lê arquivos e respostas com limite durante a leitura (8 MiB), aplica backoff também a MIME/tamanho/transporte inválidos, limita registros de falhas e compartilha downloads concorrentes. O índice em disco é carregado uma vez por instância; gravações atualizam o índice e mantêm 300 arquivos/200 MiB, sem enumerar todo o diretório após cada miniatura.
- GameDetails foi revisado e seus testes existentes continuaram passando; não houve necessidade de mudança adicional neste refinamento.

## Medições

Fixture: 1.930 jogos, 1.266 Family, 14 Installed. Quarenta mudanças alternadas de busca; art locator falso para contar solicitações. Execuções isoladas antes/depois, mesmo host e configuração Release:

| Métrica | Antes | Depois |
| --- | ---: | ---: |
| Tempo das 40 buscas | 85,677 ms | 75,795 ms |
| Bytes alocados na thread durante buscas | 10.142.248 | 4.611.400 |
| Consultas de arte na inicialização | 1.931 | 1 |
| Consultas de arte durante buscas | 57.220 | 0 |

Redução observada de 54,5% nas alocações do cenário. Tempo é uma amostra diagnóstica, não garantia de ganho em hardware diferente; uma execução intermediária junto de outros testes foi mais lenta. O contador de consultas e os testes de identidade dos cartões validam a redução de trabalho independentemente do tempo.

Imagem PNG sintética 1.920×1.080: 60 consumidores simultâneos receberam o mesmo bitmap de 160×90 em 10,266 ms, com uma única decodificação. Tamanho estimado dos pixels: 57.600 bytes na miniatura, contra 3.240.000 bytes para a decodificação anterior de 1.200×675 (56,25 vezes menor). Testes de LRU mediram o orçamento e a reutilização; o limite do cache não equivale à memória total do processo ou da GPU.

## Validação e contratos

- 44 testes focados UI/ViewModels/arte passaram: busca, reuse, geração/conta, refresh falho/tardio, sorteio pendente, ciclo anti-repeat, Undo, histórico, filtros/localização, falhas HTTP/arquivo, cache offline, coalescência, limite do disco, decode, LRU e reciclagem.
- 18 testes Domain passaram, incluindo três novas regressões de persistência. Falha de commit é simulada com destino bloqueado por diretório temporário, sem depender de locking específico do Windows.
- Artefatos: `artifacts/validation/performance/refinement-before.trx`, `refinement-after-isolated.trx`, `refinement-final.trx`, `refinement-domain.trx`.
- Propriedades novas para UI: `AdvancedFilterCount` e `HasAdvancedFilterCount` contam coleção/Deck/Mac/grupo de categorias; `CachedArtwork.DecodePixelWidth` é opcional, zero escolhe automaticamente. `SelectionPreferences.LastSaveError` alimenta o status do MainViewModel.

Limites: não se mediram FPS, tempo de rede real, GPU ou o preview ao vivo nesta frente. O histórico legado do SelectionEngine continua global; `RecentGameExclusionCount` permanece zero por padrão e não tem controle no workspace. Backlog/histórico visível e preferências anti-repeat continuam separados por conta. Os dois arquivos de persistência (engine e backlog) não formam uma transação conjunta; uma falha no segundo não publica nova seleção na UI, mas pode deixar a entrada no histórico interno do engine. O índice do cache em disco é destinado à instância do aplicativo e não coordena vários processos escritores.
