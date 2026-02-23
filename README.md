# SteamBacklogPicker

SteamBacklogPicker é um app desktop para sortear o próximo jogo da sua biblioteca Steam sem depender de serviços em nuvem. Os dados são lidos localmente do cliente Steam.

## Capacidades principais

- Descoberta e filtragem da biblioteca Steam (incluindo bibliotecas compartilhadas).
- Cache local de metadados para uso offline.
- Sorteio com filtros por coleção, tags e status de instalação.
- Cartão do jogo sorteado com indicação de estado e ação de abertura.
- Telemetria opcional e diagnósticos locais.

## Requisitos por plataforma

| Item | Windows | Linux |
| --- | --- | --- |
| SO | Windows 10 21H2+ ou Windows 11 | Distribuição x64 com desktop moderno (GNOME/KDE/XFCE) |
| Runtime | .NET 8 Desktop Runtime / SDK para build local | .NET 8 SDK para execução/build local |
| Steam | Cliente Steam instalado com acesso aos manifests em `steamapps` | Cliente Steam instalado com acesso aos manifests em `steamapps` |
| Hardware | CPU 64-bit, 4 GB RAM, 512 MB livres | CPU 64-bit, 4 GB RAM, 512 MB livres |

## Instalação e execução (passos paralelos)

| Etapa | Windows | Linux |
| --- | --- | --- |
| 1. Clonar | `git clone https://github.com/Jpkovas/SteamBacklogPicker.git` | `git clone https://github.com/Jpkovas/SteamBacklogPicker.git` |
| 2. Entrar no diretório | `cd SteamBacklogPicker` | `cd SteamBacklogPicker` |
| 3. Restaurar dependências | `dotnet restore SteamBacklogPicker.sln` | `dotnet restore SteamBacklogPicker.sln` |
| 4. Build | `dotnet build SteamBacklogPicker.sln -c Release --no-restore` | `dotnet build SteamBacklogPicker.sln -c Release --no-restore` |
| 5. Executar app | `dotnet run --project src/Presentation/SteamBacklogPicker.UI/SteamBacklogPicker.UI.csproj` | `dotnet run --project src/Presentation/SteamBacklogPicker.Linux/SteamBacklogPicker.Linux.csproj` |

## Distribuição

- **Windows**: releases com instalador (Squirrel/MSIX) para uso final.
- **Linux**: release publica pacote Linux x64 autoexecutável (`.AppImage` compatível com fluxo de update), feed `linux-appimage-update.json` e checksum SHA-256 para atualização segura.

## Resolução de instalação Steam no Linux

A descoberta da pasta principal do Steam no Linux segue prioridade explícita com validação por manifesto `steamapps/libraryfolders.vdf` em cada candidato, evitando falso-positivo:

1. `STEAM_PATH` (quando definido e válido).
2. Caminhos tradicionais: `~/.steam/steam`, `~/.steam/debian-installation`, `~/.local/share/Steam`.
3. Caminhos de sandbox/pacote: Flatpak (`~/.var/app/com.valvesoftware.Steam/.local/share/Steam`, `~/.var/app/com.valvesoftware.Steam/data/Steam`) e Snap (`~/snap/steam/common/.local/share/Steam`).


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
4. Para autoatualização, mantenha `SBP_LINUX_UPDATE_FEED_URL` apontando para o `linux-appimage-update.json` da release/canal desejado.
