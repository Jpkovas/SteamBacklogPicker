# Journey parity checklist (Windows WPF x Linux Avalonia x macOS SwiftUI)

Este checklist mapeia as funcionalidades visíveis da tela principal WPF (`MainWindow.xaml`) e acompanha a paridade da janela Avalonia (`MainWindow.axaml`) e do cliente macOS SwiftUI (`ContentView.swift`).

## Blocos visíveis mapeados da UI WPF

| Bloco visível | Elementos da UI WPF | Paridade Avalonia | Paridade macOS SwiftUI |
| --- | --- | --- | --- |
| Filtros de seleção | `RequireInstalled`, `ExcludeDeckUnsupported`, tipos de conteúdo, lojas, coleção (`ComboBox`) | ✅ Implementado com bindings diretos em `Preferences.*` | ✅ Implementado em SwiftUI com preferências persistidas e coleção Steam real |
| Ações principais | Botões para atualizar biblioteca e sortear | ✅ Botões `RefreshCommand` e `DrawCommand` | ✅ Botões `Refresh library`/`Draw` e comandos de menu equivalentes |
| Estado da elegibilidade | Mensagem de status com contagem/resultado de filtros e sorteio | ✅ `StatusMessage` exibido em seção dedicada | ✅ Status localizado com contagens e resultado do sorteio |
| Detalhes do jogo | Título sorteado, loja/origem, arte de capa, estado de instalação e tags | ✅ Bloco de detalhes com os mesmos bindings essenciais | ✅ Painel SwiftUI com título, Steam badge, arte, instalação e chips |
| Ações de execução | Botões de jogar e instalar com habilitação condicional | ✅ `LaunchCommand`/`InstallCommand` com `CanLaunch`/`CanInstall` | ✅ `steam://run`/`steam://install` com a mesma matriz de habilitação |
| Idioma | Alternância PT-BR/EN-US com atualização dinâmica de texto | ✅ Botões de idioma + atualização de recursos no bootstrap Linux | ✅ Controle BR/US atualiza janela, status e comandos |

## Checklist de jornadas (paridade funcional)

- [x] **Abrir app**: janelas Windows/Linux/macOS inicializam o estado principal e disparam carregamento inicial.
- [x] **Carregar biblioteca**: ação de atualização ligada ao botão de atualização em todas as plataformas.
- [x] **Filtrar biblioteca**: controles de instalado, Steam Deck, tipos de conteúdo, loja e coleção alteram o conjunto elegível.
- [x] **Sortear jogo**: ação de sorteio disponível no painel de filtros.
- [x] **Exibir elegibilidade/resultado**: mensagem de status mostra estados de carregamento, filtros e sorteio.
- [x] **Inspecionar detalhes**: dados do jogo selecionado (título, instalação, tags e origem) renderizados no painel direito.
- [x] **Acionar jogar/instalar**: botões ligados ao mesmo comportamento `steam://run` e `steam://install`.
- [x] **Trocar idioma**: recursos da janela atualizados entre PT-BR e EN-US.

## Validação executada nesta entrega

1. Revisão estática dos bindings e comandos dos front-ends Windows, Linux e macOS.
2. Testes de apresentação no cliente Linux para comandos mínimos e cobertura de bindings esperados no XAML.
3. Verificação do bootstrap Linux para atualização dinâmica dos recursos de idioma.
4. Testes Swift do cliente macOS para VDF, appinfo, fallback de nomes, biblioteca, coleções, filtros, comandos e notificação.
5. Validação runtime do `.app` macOS com Computer Use registrada em `docs/testing/macos-port-checklist.md`.
