# Code review e preparação da release 0.5.0

Revisão das alterações desde `c45eb915f63b0855988eca9d9c1bcb01dc42b86e`, incluindo arquivos novos. Três frentes independentes revisaram catálogo/domínio, AppCore/WPF e macOS/Avalonia. A integração revisou descoberta local, parsers, telemetria e os workflows de publicação. Os achados reproduzidos foram corrigidos antes da publicação.

## Correções da revisão

| Área | Problema | Correção e evidência |
| --- | --- | --- |
| Family | Falha de gravação recuperava direitos antigos, mesmo depois de resposta vazia válida | A resposta autoritativa mais recente prevalece em memória; logout/revogação invalidam o fallback. Regressões com SQLite/IO indisponível. |
| SteamKit | `RequestStop` depois de descarte lançava exceção | Encerramento idempotente e sincronizado com o descarte do token. |
| Histórico | Identificador nulo impedia inicialização | Normalização do identificador e compatibilidade do campo legado `AppId`. |
| AppCore | Conta anterior permanecia visível quando a descoberta da nova conta falhava | Limpeza da conta anterior antes da consulta; falha na mesma conta preserva seus dados. |
| Snapshot | Campos nulos escapavam da recuperação | Identificadores inválidos descartados, título substituto e listas vazias normalizados. |
| WPF/Avalonia | QR abaixo do limite de caracteres ainda excedia a capacidade do codificador | Tratamento específico de `DataTooLongException`; próximo desafio válido continua renderizando. |
| Interface | Refresh sem alterações mantinha “carregando” | Restaura resumo sem reconstruir a lista. |
| Modo offline | Toast recebia URL CDN | Notificações recebem somente arte local em modo offline. |
| Propriedade | Family antigo apagava evidência local de propriedade | Preserva propriedade local confirmada. |
| Appinfo | Footer real de quatro bytes era mal interpretado; no Mac perdia todos os metadados | Parser e fixtures usam AppID=0 sem campo size; versões 39–42 e VSZa cobertas. |
| macOS | Cancelar refresh apagava coleção persistida ou aceitava resultado atrasado | Cancelamento preserva preferências e descarta resultado tardio. |
| macOS | UTF-8 inválido em string visual descartava metadados independentes | Substituição Unicode nas strings visuais. |
| Testes Swift | Três caracteres em Windows-1252 impediam compilar os testes no runner macOS | Arquivo convertido para UTF-8; cenários e asserções preservados. |
| Release | Publicação Linux ocorria depois de tornar a release pública; Windows/macOS não tinham o mesmo fluxo | Workflow por tag valida as plataformas, reúne todos os pacotes em rascunho, verifica checksums e publica o conjunto completo. |

Detalhes AppCore/WPF: [relatório independente](release-review-appcore-wpf.md). O ajuste visual e os benchmarks anteriores estão no [relatório de refino](refinement/REFINAMENTO.md).

## Verificações locais após integração

- Build Release da solução, com avisos tratados como erros: zero avisos e zero erros.
- **368 aprovados, zero falhas, 11 ignorados por dependerem de Linux**, total de 379 execuções incluindo múltiplos frameworks.
- `actionlint` 1.7.12 validou os três workflows. O binário foi obtido da release oficial e conferido pelo checksum; sem shellcheck/pyflakes locais. Git diff sem erros de whitespace.
- A validação interativa Windows das rodadas anteriores inclui biblioteca real, busca, filtros, backlog/desfazer, alinhamento dos cards e tamanho mínimo 980 × 680.
- O pacote Windows 0.5.0 também foi aberto por Computer Use: inicialização, término da sincronização sem alterações, busca com dois resultados e alinhamento das sugestões conferidos.
- A validação nativa Linux/macOS fica nos jobs obrigatórios do GitHub Actions. A publicação depende do sucesso desses jobs e da presença dos pacotes e checksums.

## Publicação e limites

`release.yml` reutiliza o CI do mesmo commit e o empacotamento Linux; somente o job final recebe permissão de escrita de conteúdo. A release pública existente não é sobrescrita numa reexecução. A implementação segue a [documentação de workflows reutilizáveis do GitHub](https://docs.github.com/en/actions/how-tos/reuse-automations/reuse-workflows).

Windows não tem Authenticode. O pacote macOS tem assinatura ad-hoc e arquitetura indicada no nome, sem Developer ID/notarização; sua interface SwiftUI não recebeu o redesenho WPF/Avalonia. Não há chave de assinatura do feed Linux configurada no repositório nesta revisão; atualização automática permanece dependente da configuração explícita de confiança. Downloads manuais continuam disponíveis.

Se o disco impedir a gravação Family, a resposta recente em memória dura apenas a sessão. Depois de reiniciar, somente o que foi persistido e suas regras de TTL estão disponíveis. Backlog e preferências usam arquivos separados, sem transação conjunta ou coordenação de múltiplos processos escritores. Esses limites não são apresentados como garantias de licença; o cliente Steam decide o acesso ao jogo.
