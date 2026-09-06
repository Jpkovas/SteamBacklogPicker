# Code review para release 0.5.0 — AppCore e WPF

Revisão manual do patch em relação a `c45eb91`, incluindo serviços novos, composição, ViewModels, XAML/estilos, ciclo de vida WPF, imagens e testes relacionados. Sem scan formal de segurança, commit ou publicação nesta frente.

## Achados corrigidos

1. **P2 — Toast ignorava o modo offline.** `MainViewModel.ApplySelection` entregava a URL CDN ao serviço de notificações mesmo com `NetworkEnabled=false`; o Windows podia baixar a imagem por fora do cache. Agora a notificação recebe somente arte local nesse modo. O teste verifica modo offline e online com serviço falso, sem enviar notificações reais.
2. **P2 — Falha de descoberta durante troca de conta mantinha a biblioteca anterior.** `CatalogLibraryService` só publicava a nova identidade depois do retorno do provedor. Agora descarta o snapshot da conta anterior antes de consultar a nova conta; uma exceção de IO não deixa cartões antigos visíveis. Refresh falho da mesma conta continua preservando seus dados.
3. **P2 — Snapshot local com nulos escapava da recuperação.** `GameEntry.Id=null` causava NullReferenceException e outros campos nulos chegavam à UI. A normalização descarta identificadores inválidos, gera título substituto e restaura coleções vazias; dados restaurados continuam com instalação/propriedade desconhecidas.
4. **P2 — QR acima da capacidade interrompia o dispatcher.** O limite de 4.096 caracteres não garantia que QRCoder com ECC Q pudesse codificar a entrada. A geração WPF foi isolada e trata `DataTooLongException`; payload de 4.000 caracteres é rejeitado, enquanto o QR normal continua produzindo bitmap congelado. O QR permanece somente em memória.
5. **P3 — Refresh idêntico deixava “carregando” preso.** O retorno antecipado de `AcceptSnapshot` mantinha a mensagem de carregamento. Agora restaura o resumo de elegibilidade sem recriar o ItemsSource.
6. **P3 — Family stale apagava evidência local atual de propriedade.** Ao juntar resposta Family antiga, uma entrada local Owned virava Unknown. Agora a evidência atual local de propriedade é preservada; apps conhecidos apenas pelo Family stale continuam Unknown.

Cada caso possui regressão em `MainViewModelWorkspaceTests`, `CatalogLibraryServiceTests` ou `VisualMotionTests`.

## Validação

`SteamBacklogPicker.UI.Tests`: **127/127 aprovados**, Release, .NET SDK 8.0.424, Windows. Inclui construção de AppCore/WPF e todos os testes do projeto; execução coordenada com as outras frentes. Resultado: `artifacts/validation/release-review/release-review-ui.trx`. `git diff --check` passou nas áreas revisadas.

A hipótese de subcontagem para PNG RGBA64 foi descartada: neste pipeline o WPF retorna bitmap de 32 bits. A mudança especulativa e seu teste foram removidos após essa evidência.

## Limites e verificações restantes

- Não houve sessão Steam real, chamadas HTTP externas, toast real ou validação visual interativa nesta revisão. Renderização geral usa a validação visual anterior e os testes WPF de template/QR/imagens; publicação final continua com o responsável pela release.
- O cache offline de metadados já converte falhas IO/SQLite esperadas em resultados indisponíveis; não foi adicionado um catch genérico que esconderia erros de programação.
- Permanecem os limites documentados do armazenamento: BacklogStore e settings do SelectionEngine usam arquivos distintos; não há transação conjunta nem coordenação de múltiplos processos escritores. A identidade do backlog visível permanece separada por conta.
- Nenhum bloqueante conhecido ficou aberto nas áreas corrigidas. CI completo, pacotes e smoke test do artefato de release devem usar o estado final integrado.
