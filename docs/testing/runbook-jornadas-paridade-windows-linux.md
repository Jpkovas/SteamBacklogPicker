# Runbook manual – Jornadas equivalentes (Windows/Linux)

Objetivo: executar jornadas de usuário críticas e validar paridade funcional entre clientes.

## Jornadas e critérios de aceite

| Jornada | Passos (ambas plataformas) | Resultado esperado |
| --- | --- | --- |
| Filtrar biblioteca | Abrir filtros, aplicar tags + instalado, confirmar contagem de resultados | Contagem e comportamento coerentes com os filtros aplicados |
| Sortear jogo | Acionar sorteio, repetir 3 vezes | Cada sorteio retorna título válido sem travar a UI |
| Abrir jogo | Com um jogo sorteado, acionar `Abrir no Steam` | Cliente Steam recebe comando para o appid correto |
| Modo offline | Fechar rede/usar Steam offline, repetir sorteio e leitura de biblioteca | App usa cache local e informa fallback sem erro crítico |
| Logs e diagnóstico | Forçar erro controlado (ex.: caminho inválido) e revisar log | Log local registra falha com contexto útil |

## Procedimento de execução

1. Executar todas as jornadas no Windows.
2. Repetir os mesmos passos no Linux.
3. Comparar saídas e classificar cada jornada como:
   - **Paridade total**
   - **Paridade com diferença temporária**
   - **Sem paridade**

## Regra para diferenças temporárias

Quando houver "Paridade com diferença temporária", abrir item em `CHANGES.md` com a convenção de portabilidade definida em `docs/requirements.md` (incluindo data-alvo de convergência).

## Evidências mínimas

- Tabela preenchida com status por jornada.
- Captura/log para qualquer diferença encontrada.
- Link para item de portabilidade no changelog quando aplicável.
