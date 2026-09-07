# Refinamento do catálogo e da sincronização — 2026-09-06

Escopo: `src/Integration/SteamCatalog`, `CatalogLibraryService` e seus testes. Sem alterações de XAML, MainViewModel, ArtworkCache, BacklogStore ou autenticação. Nenhuma sessão Steam real foi aberta, encerrada ou automatizada nesta frente.

## Problemas reproduzidos e correções

- **Atualizações redundantes:** uma biblioteca sintética de 2.000 jogos, com títulos e metadados já completos, emitia 102 eventos de biblioteca e 104 de status. O teste de regressão reproduziu a falha antes da correção. Agora entradas idênticas conservam a referência; publicações repetidas são descartadas dentro da mesma geração, e status idêntico não gera evento. A primeira publicação de cada geração continua obrigatória.
- **Reprocessamento por lote:** cada lote de 20 percorria toda a biblioteca e a cópia destinada à persistência. Uma projeção indexada por app ID agora aplica apenas os resultados recebidos. A lista publicada é uma cópia imutável para os lotes seguintes. Mudanças rápidas são agrupadas em intervalos de 250 ms, com publicação final garantida; cancelamento descarta alterações pendentes da geração antiga.
- **Leitura SQLite por jogo:** o caminho anterior abria uma conexão por app. A capacidade opcional `IBatchCatalogCache` usa uma conexão por chamada, com consultas parametrizadas de até 256 IDs. Cache fresco e modo offline usam esse resultado diretamente. Entradas expiradas ou ausentes no modo online ainda são verificadas sob o bloqueio compartilhado, preservando a deduplicação entre chamadas simultâneas. Falha de leitura em lote pode recorrer à leitura individual.
- **Escrita local duplicada:** ao terminar um refresh sem mudanças de metadados locais, o coordenador evita serializar e substituir novamente o snapshot que acabou de salvar.

TTL positivo/negativo, concorrência e ritmo de rede, exclusões familiares, revogação, isolamento por conta/idioma e cancelamento no shutdown permanecem cobertos. Os limites de rede não foram aumentados; um catálogo sem cache ainda depende da latência e dos limites das fontes Steam.

## Medição antes/depois

Windows, .NET 8 Release, mesma máquina e fixtures sintéticas, sem acesso de rede. Cada medição abaixo veio de uma execução isolada do respectivo teste; o tempo exclui o preenchimento inicial do banco. São medidas diagnósticas de uma execução, com JIT e IO local, e não uma estimativa de FPS da UI ou um benchmark estatístico. A contagem de eventos é determinística; o tempo e a alocação variam entre execuções.

| Cenário com 2.000 jogos | Antes | Depois |
| --- | ---: | ---: |
| Eventos de biblioteca, conteúdo inalterado | 102 | 1 |
| Eventos de status, conteúdo inalterado | 104 | 4 |
| Tempo do coordenador | 121,8 ms | 99,1 ms |
| Bytes alocados no processo durante o refresh | 11.300.456 | 6.293.168 |
| Leitura offline do catálogo SQLite | 1.295,1 ms | 86,7 ms |
| Bytes alocados no processo durante a leitura | 13.848.112 | 4.781.872 |
| Chamadas de rede na leitura offline | 0 | 0 |

O teste de cache verifica também que dois pedidos, offline e online com dados frescos, usam exatamente duas leituras em lote e nenhuma leitura individual ou chamada de rede.

## Verificação

**60 testes aprovados:** 35 em `SteamCatalog.Tests` e 25 em `CatalogLibraryServiceTests`, sem avisos de compilação nas execuções registradas.

Novos casos cobrem biblioteca grande inalterada, leitura de 2.000 apps atravessando vários lotes SQL, uso do caminho em lote com cache fresco, recuperação após falha desse caminho, cancelamento de leitura em lote bloqueada por outra conexão SQLite, agrupamento com relógio controlado, imutabilidade das listas publicadas e descarte de mudanças ainda não publicadas ao cancelar. A suíte anterior mantém cobertura de TTL, cache negativo, deduplicação global, idioma, identidade, logout, respostas familiares vazias/excluídas/revogadas e shutdown.

Comandos, após configurar o SDK local da auditoria:

```powershell
dotnet test tests/Integration/SteamCatalog.Tests/SteamCatalog.Tests.csproj -c Release --no-restore
dotnet test tests/Presentation/SteamBacklogPicker.UI.Tests/SteamBacklogPicker.UI.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~CatalogLibraryServiceTests
```

Evidência em `artifacts/audit/implementation-tests/refinement-catalog/`: `coordinator-before.trx` registra a regressão reproduzida; `sqlite-before.trx` registra a leitura original; `coordinator-after-isolated.trx` e `sqlite-after-isolated.trx` contêm os valores finais da tabela; `catalog-final.trx` e `coordinator-final.trx` contêm as suítes completas desta frente.

## Observação manual anterior

O responsável pelo teste da UI relatou aprovação de QR pelo usuário e uma biblioteca real com 1.930 entradas, incluindo 1.266 familiares; The Wolf Among Us apareceu como compartilhado e não instalado. Essa observação ocorreu em um preview anterior a este refinamento, com o mesmo transporte de autenticação/Family. Não foi repetida por esta frente e não substitui a revisão visual do preview atualizado. Nenhum QR, token ou identificador de conta foi incluído neste registro.
