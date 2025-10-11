# Relatório de Desempenho

## Escopo
Medições coletadas em ambiente Linux (container CI) utilizando `.NET SDK 8.0.120` com foco na inicialização e consultas do adaptador Steam (simulado pelos testes de integração).

## Metodologia
- Comando: `/usr/bin/time -v dotnet test tests/Integration/SteamClientAdapter.Tests/SteamClientAdapter.Tests.csproj`
- Os testes cobrem fluxo de inicialização do `SteamClientAdapter`, incluindo chamadas de fallback VDF.
- Execução realizada após `dotnet restore`, sem outras cargas em paralelo.

## Resultados
| Métrica | Valor |
| --- | --- |
| Tempo de parede | 8.61 s |
| CPU usuário | 11.28 s |
| CPU sistema | 2.41 s |
| Uso máximo de RAM | 172 MB |
| Trocas de contexto voluntárias | 7,923 |
| Trocas de contexto involuntárias | 14,246 |

> Fonte: saída do `/usr/bin/time -v` capturada em 27/05/2024 durante a execução do comando descrito acima.

## Observações
- O tempo total inclui restauração incremental (quando necessária) e carga do runtime .NET.
- Recomenda-se repetir a medição após alterações na camada de integração ou inclusão de novos parsers VDF.
- Para cenários de UI, usar uma estação Windows com `dotnet test` habilitando `SteamBacklogPicker.UI.Tests`.
