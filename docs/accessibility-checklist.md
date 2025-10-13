# Checklist de Acessibilidade

Este checklist resume os itens verificados manualmente na tela principal da aplicação WPF.

## Controles interativos
- [x] Os botões "Atualizar biblioteca", "Sortear", "Jogar" e "Instalar" possuem `AutomationProperties.Name` descritivo.
- [x] O botão "Sortear" expõe `AutomationProperties.HelpText` explicando o impacto do comando.
- [x] Os filtros de conteúdo (CheckBoxes) e a seleção de coleção (ComboBox) fornecem nomes acessíveis consistentes com o texto visível.
- [x] Comandos estão disponíveis via teclado, respeitando a navegação padrão do WPF.

## Comunicação assistiva
- [x] A mensagem de status usa `AutomationProperties.LiveSetting="Assertive"` para leitura imediata.
- [x] O painel de filtros e o painel de detalhes possuem nomes de região para facilitar a navegação via leitores de tela.
- [x] O título e a capa do jogo selecionado expõem `AutomationProperties.Name` para leitores de tela anunciarem o conteúdo.

## Ajustes de conteúdo
- [x] O status de instalação do jogo é exibido em texto legível com atualização em tempo real.
- [x] A lista de tags do jogo é apresentada como itens de texto, preservando leitura sequencial.
- [x] A cor de fundo e contraste seguem a paleta padrão Steam com texto em alto contraste.

## Testes e validação
- [x] Testes automatizados verificam atributos críticos de acessibilidade.
- [x] Checklist revisado antes de cada entrega para garantir regressões mínimas.
