# Steam Backlog Picker

Aplicativo desktop para escolher o próximo jogo da biblioteca Steam. Lê a instalação local, aplica filtros e abre o jogo ou a instalação pelo cliente Steam. Windows e Linux compartilham o catálogo, a sincronização e o novo espaço de biblioteca; macOS tem uma implementação SwiftUI própria, com uma interface e um conjunto de recursos diferentes.

## O que está implementado

| Recurso | Windows — WPF | Linux — Avalonia | macOS — SwiftUI |
| --- | --- | --- | --- |
| Leitura de manifests, perfil e caches locais Steam | Sim | Sim | Sim |
| Sorteio, filtros de instalação, conteúdo, coleção e compatibilidade | Sim | Sim | Sim |
| Descobrir, Biblioteca pesquisável, Histórico e Configurações | Sim | Sim | Interface anterior de filtros e sorteio |
| Catálogo automático de nomes/metadados, cache SQLite por idioma | Sim | Sim | Resolução de nomes via loja, cache JSON por idioma |
| Login por QR e consulta autenticada de Steam Families | Sim | Sim | Não implementado |
| Backlog por conta: Quero jogar, Jogando, Concluído, Depois, Oculto e desfazer | Sim | Sim | Não implementado |
| Capas carregadas de forma assíncrona com cache em disco e cancelamento | Sim | Sim | Carregamento próprio de capas |
| Modo local/offline configurável na interface | Sim | Sim | Leitura local e reaproveitamento do cache de nomes; sem o novo controle de rede |
| Pacote gerado pelos scripts | ZIP portátil x64 com runtime incluído | AppImage x64 ou executável portátil com runtime incluído | ZIP de desenvolvimento contendo `.app`, na arquitetura do build |

A presença de um título no cache não comprova uma licença atual. **Instalação e acesso são independentes**: o estado instalado vem de evidência local de instalação completa; compartilhamento familiar pertence à informação de acesso. `LastOwner`, histórico e appinfo global não transformam um jogo em comprado ou compartilhado. O filtro “Instalados” não inclui um jogo apenas por ser familiar. Ao jogar ou instalar, o cliente Steam confirma licença, disponibilidade de cópias e restrições da conta.

## Uso no Windows e Linux

1. Abra o app com o Steam instalado. A biblioteca local e o cache aparecem primeiro; nomes e detalhes conhecidos são atualizados em segundo plano. A sincronização de metadados online começa habilitada e pode ser desativada em **Configurações**; essa escolha é preservada.
2. Em **Descobrir**, use a busca, os filtros e o sorteio. As sugestões excluem a seleção atual. A opção de evitar repetições percorre os candidatos antes de iniciar outro ciclo.
3. Em **Biblioteca**, pesquise pelo nome ou AppID, filtre jogos próprios/familiares e estados do backlog. Selecione um jogo para ver suas ações e registrar seu estado. **Desfazer** recupera a última alteração do backlog da conta atual.
4. Em **Histórico**, consulte os sorteios ou limpe a lista. Preferências, backlog e histórico são persistidos localmente; as decisões do backlog são separadas por conta.
5. Para consultar a família, abra **Configurações → Conectar Steam** e aprove o QR pelo aplicativo Steam. Essa ação habilita a rede. O app não pede sua senha: o desafio QR e as credenciais da sessão ficam somente em memória. Após encerrar o app, um novo acesso autenticado requer login novamente. **Cancelar** interrompe a operação; **Desconectar** encerra a sessão e invalida os dados familiares daquela sessão.

Atalhos no Windows/Linux: **Ctrl+F** abre a Biblioteca e foca a busca; **F5** atualiza; **Esc** fecha filtros avançados e cancela uma conexão em andamento. No Linux, também cancela a sincronização. Filtros avançados ficam ocultos inicialmente e não cobrem as outras páginas.

A leitura local pode cobrir apenas parte da biblioteca familiar. A consulta autenticada complementa essa informação, mas não garante que todos os jogos possam ser abertos naquele momento. Troca de conta, desconexão e revogação impedem que permissões antigas sejam reaplicadas à conta errada.

## Cache e funcionamento offline

No Windows/Linux, o app mantém seu próprio catálogo SQLite, snapshots locais e cache de capas no diretório de dados do usuário, sob `SteamBacklogPicker`. Metadados são separados por AppID/idioma; resultados válidos têm TTL de sete dias e ausências confirmadas de seis horas; falhas de rede não são gravadas como jogo inexistente. Informações familiares são separadas por conta/idioma e têm validade curta, de trinta minutos; dados antigos podem ser apresentados como desatualizados quando a rede falha, mas uma resposta de acesso negado invalida esse uso. O cache não contém tokens de login e não modifica os arquivos do cliente Steam.

Desative a rede em Configurações para usar a informação local disponível. O catálogo e as capas já armazenadas continuam úteis; nomes ausentes podem permanecer como `App <id>` e a cobertura familiar pode ser incompleta. Falhas parciais de atualização preservam dados anteriores quando ainda são aplicáveis. Um snapshot recuperado sem evidência atual de instalação/licença não é tratado como prova de acesso.

No macOS, nomes ausentes podem ser consultados em `appdetails` da loja Steam. O cache JSON dura sete dias e é separado por idioma. As consultas têm concorrência limitada, cancelamento e prazo global de 25 segundos. Os parsers VDF/appinfo validam limites e truncamentos; o appinfo comprimido usa zstd oficial fixado no SwiftPM. Essas melhorias não incluem o novo login familiar, catálogo SQLite ou backlog da interface Windows/Linux.

## Executar um pacote

### Windows

Extraia **todo** o ZIP `SteamBacklogPicker-<versão>-win-x64.zip` e execute `SteamBacklogPicker.UI.exe` dentro da pasta extraída. O runtime .NET Desktop está incluído; não é preciso instalar .NET separadamente. O app continua usando o perfil do usuário para configurações/cache, mesmo sendo portátil.

Compare o resultado abaixo com o arquivo `.zip.sha256` entregue junto do pacote:

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath .\SteamBacklogPicker-1.0.0-win-x64.zip
```

A interface WPF usa a seleção automática de renderização do Windows. A aceleração foi revalidada no refino da interface; forçar software deixou de ser o padrão. Em drivers problemáticos, use `SBP_SOFTWARE_RENDERING=1` (ou o alias legado `SBP_HARDWARE_RENDERING=0`). Microinterações duram 100–190 ms, não executam loops em repouso e respeitam a preferência do Windows por reduzir animações; `SBP_REDUCED_MOTION=1` também as desativa.

O ZIP não recebe assinatura Authenticode pelo script atual. O checksum detecta alteração dos bytes baixados; não substitui a autenticação do publicador. O atualizador Squirrel legado permanece desativado. Para atualizar, extraia a nova versão em outra pasta.

### Linux

O AppImage exige Linux x64, sessão gráfica e as bibliotecas nativas do desktop. Valide o SHA-256 publicado e então execute:

```bash
sha256sum SteamBacklogPicker-1.0.0-linux-x64.AppImage
chmod +x SteamBacklogPicker-1.0.0-linux-x64.AppImage
./SteamBacklogPicker-1.0.0-linux-x64.AppImage
```

O runtime .NET está incluído. A descoberta considera `STEAM_PATH`, `XDG_DATA_HOME/Steam`, instalações tradicionais, Flatpak e Snap, validando cada candidato pelo manifesto `steamapps/libraryfolders.vdf`. Acesso aos arquivos ainda depende das permissões do sistema.

O auto-update Linux exige `SBP_LINUX_UPDATE_FEED_URL` e `SBP_LINUX_UPDATE_PUBLIC_KEY` com uma chave PEM confiável do publicador. O feed e seu hash são verificados; os bytes pendentes são novamente validados antes da substituição. O script de release só assina quando uma chave real está configurada. A opção `SBP_ENABLE_UNSIGNED_LINUX_UPDATE_FEED=true` é destinada a testes locais, não à distribuição confiável.

### macOS

A implementação nativa exige macOS 13 ou superior. O pacote de desenvolvimento contém uma `.app` produzida para a arquitetura da máquina que o compilou; não é um binário universal. Por padrão, a assinatura é ad hoc. Developer ID e notarização exigem credenciais reais e etapas de distribuição adicionais. Não há atualizador macOS implementado.

## Compilar e testar

Para Windows/Linux, use o SDK .NET 8. Para a implementação SwiftUI, use macOS com Swift 5.9 ou superior/Xcode compatível. O Steam é necessário para validar dados e ações reais; os testes automatizados usam fixtures e serviços simulados.

```powershell
# Na raiz do repositório, em Windows:
dotnet restore SteamBacklogPicker.sln
dotnet build SteamBacklogPicker.sln -c Release --no-restore
dotnet test SteamBacklogPicker.sln -c Release --no-build
dotnet run --project src/Presentation/SteamBacklogPicker.UI/SteamBacklogPicker.UI.csproj
```

```bash
# Linux: compile a apresentação Avalonia e execute sua suíte.
dotnet restore tests/Presentation/SteamBacklogPicker.Linux.Tests/SteamBacklogPicker.Linux.Tests.csproj
dotnet test tests/Presentation/SteamBacklogPicker.Linux.Tests/SteamBacklogPicker.Linux.Tests.csproj -c Release --no-restore
dotnet run --project src/Presentation/SteamBacklogPicker.Linux/SteamBacklogPicker.Linux.csproj

# macOS: SwiftPM resolve a dependência zstd fixada em Package.resolved.
swift package resolve
swift build
swift test
swift run SteamBacklogPickerMac
```

O workflow comum testa .NET em Windows/Linux; o job Windows executa a suíte WPF. Os testes Avalonia headless verificam controles, bindings, QR e capas sem abrir janelas. Os testes de troca de binário Linux são explicitamente ignorados em Windows. O job macOS compila/testa o pacote Swift e gera a `.app` de desenvolvimento. Uma compilação cruzada ou um teste headless não substitui o smoke test no sistema de destino.

## Gerar pacotes

Os comandos abaixo usam diretórios novos de saída e não removem resultados anteriores.

```powershell
# Windows: restore para win-x64, publish self-contained, pasta portátil, ZIP e SHA-256.
./scripts/package-windows.ps1 -Version 1.0.0 -OutputDirectory ./artifacts/windows-1.0.0

# Opcional: selecionar explicitamente um SDK sem alterar a instalação global.
./scripts/package-windows.ps1 -Version 1.0.0 -OutputDirectory ./artifacts/windows-isolated-1.0.0 -DotnetPath ./artifacts/audit/runtime/dotnet/dotnet.exe
```

```bash
# Linux: APPIMAGETOOL_PATH produz AppImage; sem ele, sai um executável portátil.
bash scripts/package-linux-appimage.sh 1.0.0 ./artifacts/linux-1.0.0

# macOS: assinatura ad hoc por padrão; MACOS_SIGNING_IDENTITY usa identidade existente.
bash scripts/package-macos-app.sh 1.0.0 ./artifacts/macos-1.0.0
```

O CI de `main` disponibiliza pacotes Windows/macOS e checksums como artifacts de build, com versão de candidato `0.0.0`. Tags no formato `vMAJOR.MINOR.PATCH` acionam o workflow `Release`: testes nativos Windows/Linux/macOS e empacotamento precisam passar antes da publicação. Windows ZIP, Linux AppImage, macOS ZIP por arquitetura e checksums são reunidos em um rascunho, publicado apenas quando completo. Uma release pública existente nunca é substituída automaticamente. As notas ficam em `docs/releases/<tag>.md`.

O workflow Linux usa appimagetool e runtime fixados e verifica os digests antes de executá-los. Builds locais sem URL pública não inventam um feed de atualização: use `SBP_LINUX_DOWNLOAD_URL` quando existir um destino real. O workflow `Package Linux` também pode ser executado manualmente para gerar um candidato sem publicar. Windows continua sem assinatura Authenticode; macOS tem assinatura ad-hoc de desenvolvimento, sem Developer ID/notarização. O feed Linux só é assinado quando `SBP_LINUX_UPDATE_PRIVATE_KEY` está configurado.

## Privacidade e limites

A telemetria .NET exige consentimento explícito e começa desativada. Logs opcionais ficam no perfil do usuário; falta de permissão para esse destino não impede a inicialização. Consultas de catálogo/capas e login Steam são funcionalidades de rede separadas da telemetria. O app não armazena senha, QR ou token em seu banco de catálogo.

O protocolo `steam://` delega execução e instalação ao Steam. O app não contorna DRM, limitações do Steam Families ou requisitos de compatibilidade. A composição padrão .NET faz descoberta local sem carregar automaticamente DLLs de jogos para obter Steamworks. Notificações, acesso à instalação, login real e empacotamento precisam de validação no sistema de destino. Veja [a validação por plataforma](docs/testing/platform-fixes-validation.md) para evidências, dependências fixadas e limites da distribuição.
