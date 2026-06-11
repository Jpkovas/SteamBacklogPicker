# SteamBacklogPicker

SteamBacklogPicker é um app desktop para sortear o próximo jogo da sua biblioteca Steam. Os dados são lidos localmente do cliente Steam sempre que possível; no macOS, títulos ausentes no cache local podem ser completados pela API pública de detalhes da loja Steam e salvos em cache local.

## Capacidades principais

- Descoberta e filtragem da biblioteca Steam (incluindo bibliotecas compartilhadas).
- Cache local de metadados para uso offline.
- Sorteio com filtros por coleção, tags e status de instalação.
- Cartão do jogo sorteado com indicação de estado e ação de abertura.
- Telemetria opcional e diagnósticos locais.

## Requisitos por plataforma

| Item | Windows | Linux | macOS |
| --- | --- | --- | --- |
| SO | Windows 10 21H2+ ou Windows 11 | Distribuição x64 com desktop moderno (GNOME/KDE/XFCE) | macOS 13+ |
| Runtime | .NET 8 Desktop Runtime / SDK para build local | .NET 8 SDK para execução/build local | Xcode/Swift 5.9+ para build local |
| Steam | Cliente Steam instalado com acesso aos manifests em `steamapps` | Cliente Steam instalado com acesso aos manifests em `steamapps` | Cliente Steam instalado em `~/Library/Application Support/Steam` ou `STEAM_PATH`; a versão macOS usa manifests, perfil local, `librarycache`, `appcache/appinfo.vdf`, `cloudstorage` para coleções e fallback público `appdetails` para nomes ausentes |
| Hardware | CPU 64-bit, 4 GB RAM, 512 MB livres | CPU 64-bit, 4 GB RAM, 512 MB livres | Apple Silicon ou Intel 64-bit, 4 GB RAM, 512 MB livres |

## Instalação e execução (passos paralelos)

| Etapa | Windows | Linux | macOS |
| --- | --- | --- | --- |
| 1. Clonar | `git clone https://github.com/Jpkovas/SteamBacklogPicker.git` | `git clone https://github.com/Jpkovas/SteamBacklogPicker.git` | `git clone https://github.com/Jpkovas/SteamBacklogPicker.git` |
| 2. Entrar no diretório | `cd SteamBacklogPicker` | `cd SteamBacklogPicker` | `cd SteamBacklogPicker` |
| 3. Restaurar dependências | `dotnet restore SteamBacklogPicker.sln` | `dotnet restore SteamBacklogPicker.sln` | Não há restore externo; SwiftPM usa `Package.swift` |
| 4. Build | `dotnet build SteamBacklogPicker.sln -c Release --no-restore` | `dotnet build SteamBacklogPicker.sln -c Release --no-restore` | `swift build --product SteamBacklogPickerMac` |
| 5. Executar app | `dotnet run --project src/Presentation/SteamBacklogPicker.UI/SteamBacklogPicker.UI.csproj` | `dotnet run --project src/Presentation/SteamBacklogPicker.Linux/SteamBacklogPicker.Linux.csproj` | `swift run SteamBacklogPickerMac` em desenvolvimento local |

## Distribuição

- **Windows**: o workflow atual publica saída bruta de `dotnet publish`; instaladores Squirrel/MSIX ainda dependem de automação de empacotamento a ser restaurada. O auto-update Squirrel legado fica desativado por padrão e só roda com `SBP_ENABLE_LEGACY_WINDOWS_UPDATE=true` até existir Authenticode/pinning de certificado.
- **Linux**: release publica um AppImage Linux x64 nativo, feed `linux-appimage-update.json`, checksum SHA-256 e assinatura do feed quando a chave de release está configurada. Execuções locais do script sem `appimagetool` caem para executável portátil.
- **macOS**: a versão SwiftUI nativa vive em `src/Presentation/SteamBacklogPicker.Mac` e roda localmente via SwiftPM. Assinatura, notarização, bundle `.app` versionado e empacotamento DMG/PKG ainda não fazem parte do fluxo de release.

## Resolução de instalação Steam no Linux

A descoberta da pasta principal do Steam no Linux segue prioridade explícita com validação por manifesto `steamapps/libraryfolders.vdf` em cada candidato, evitando falso-positivo:

1. `STEAM_PATH` (quando definido e válido).
2. `XDG_DATA_HOME/Steam` (quando `XDG_DATA_HOME` estiver definido e válido).
3. Caminhos tradicionais: `~/.steam/steam`, `~/.steam/debian-installation`, `~/.local/share/Steam`.
4. Caminhos de sandbox/pacote: Flatpak (`~/.var/app/com.valvesoftware.Steam/.local/share/Steam`, `~/.var/app/com.valvesoftware.Steam/data/Steam`) e Snap (`~/snap/steam/common/.local/share/Steam`).


## Telemetria e privacidade

A telemetria é opcional. Quando ativada, apenas eventos anônimos de uso são coletados. Logs ficam localmente no diretório de dados do app e podem ser removidos pelo usuário.

## Instalação por release (Linux)

1. Abra a página de releases e baixe `SteamBacklogPicker-<versao>-linux-x64.AppImage` e `linux-appimage-update.json`.
2. Valide o checksum local antes de executar:
   ```bash
   sha256sum SteamBacklogPicker-<versao>-linux-x64.AppImage
   ```
   Compare com o campo `sha256` do feed JSON publicado na release.
3. Torne o pacote executável e rode:
   ```bash
   chmod +x SteamBacklogPicker-<versao>-linux-x64.AppImage
   ./SteamBacklogPicker-<versao>-linux-x64.AppImage
   ```
4. Para autoatualização segura, mantenha `SBP_LINUX_UPDATE_FEED_URL` apontando para o `linux-appimage-update.json` da release/canal desejado e configure `SBP_LINUX_UPDATE_PUBLIC_KEY` com a chave pública PEM que valida a assinatura do feed. Feed sem assinatura só é aceito com `SBP_ENABLE_UNSIGNED_LINUX_UPDATE_FEED=true`, reservado para testes locais.
