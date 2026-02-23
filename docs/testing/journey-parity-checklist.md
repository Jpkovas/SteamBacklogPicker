# Journey parity checklist (Windows WPF x Linux Avalonia)

Este checklist mapeia as funcionalidades visíveis da tela principal WPF (`MainWindow.xaml`) e acompanha a paridade da janela Avalonia (`MainWindow.axaml`).

## Blocos visíveis mapeados da UI WPF

| Bloco visível | Elementos da UI WPF | Paridade Avalonia |
| --- | --- | --- |
| Filtros de seleção | `RequireInstalled`, `ExcludeDeckUnsupported`, tipos de conteúdo, lojas, coleção (`ComboBox`) | ✅ Implementado com bindings diretos em `Preferences.*` |
| Ações principais | Botões para atualizar biblioteca e sortear | ✅ Botões `RefreshCommand` e `DrawCommand` |
| Estado da elegibilidade | Mensagem de status com contagem/resultado de filtros e sorteio | ✅ `StatusMessage` exibido em seção dedicada |
| Detalhes do jogo | Título sorteado, loja/origem, arte de capa, estado de instalação e tags | ✅ Bloco de detalhes com os mesmos bindings essenciais |
| Ações de execução | Botões de jogar e instalar com habilitação condicional | ✅ `LaunchCommand`/`InstallCommand` com `CanLaunch`/`CanInstall` |
| Idioma | Alternância PT-BR/EN-US com atualização dinâmica de texto | ✅ Botões de idioma + atualização de recursos no bootstrap Linux |

## Checklist de jornadas (paridade funcional)

- [x] **Abrir app**: janela Linux inicializa `MainViewModel` e dispara carregamento inicial.
- [x] **Carregar biblioteca**: `RefreshCommand` ligado ao botão de atualização.
- [x] **Filtrar biblioteca**: controles de filtro ligados ao `SelectionPreferencesViewModel`.
- [x] **Sortear jogo**: `DrawCommand` disponível no painel de filtros.
- [x] **Exibir elegibilidade/resultado**: `StatusMessage` mostra estados de carregamento, filtros e sorteio.
- [x] **Inspecionar detalhes**: dados de `SelectedGame` (título, instalação, tags e origem) renderizados no painel direito.
- [x] **Acionar jogar/instalar**: botões ligados a `LaunchCommand` e `InstallCommand`.
- [x] **Trocar idioma**: recursos da janela atualizados por evento de localização no `App.axaml.cs`.

## Validação executada nesta entrega

1. Revisão estática dos bindings e comandos dos dois front-ends.
2. Testes de apresentação no cliente Linux para comandos mínimos e cobertura de bindings esperados no XAML.
3. Verificação do bootstrap Linux para atualização dinâmica dos recursos de idioma.
