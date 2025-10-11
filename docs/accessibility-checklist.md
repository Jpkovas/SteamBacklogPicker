# Checklist de Acessibilidade

Este checklist resume os itens verificados manualmente na tela principal da aplicação WPF.

## Controles interativos
- [x] Todos os botões possuem `AutomationProperties.Name` descritivo.
- [x] Os botões com ações críticas possuem `AutomationProperties.HelpText` explicando o impacto.
- [x] O botão de favoritos é exposto como `ToggleButton` para leitores de tela anunciarem o estado.
- [x] Comandos estão disponíveis via teclado, respeitando a navegação padrão do WPF.

## Comunicação assistiva
- [x] A mensagem de status usa `AutomationProperties.LiveSetting="Assertive"` para leitura imediata.
- [x] A notificação Toast anuncia o jogo sorteado com título e imagem opcional.
- [x] O painel de filtros e detalhes possui nomes de região para facilitar a navegação via leitores de tela.

## Ajustes de conteúdo
- [x] Os campos de tamanho aceitam valores numéricos em GB e exibem dicas para interpretação.
- [x] Tags obrigatórias informam como múltiplos valores devem ser separados.
- [x] A cor de fundo e contraste seguem a paleta padrão Steam com texto em alto contraste.

## Testes e validação
- [x] Testes automatizados verificam atributos críticos de acessibilidade.
- [x] Checklist revisado antes de cada entrega para garantir regressões mínimas.
