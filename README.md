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
- **Linux**: no estado atual, não há job de release Linux no repositório gerando artefatos instaláveis. Para Linux, use execução local via `dotnet run`/build local até a esteira de empacotamento ser adicionada.

## Telemetria e privacidade

A telemetria é opcional. Quando ativada, apenas eventos anônimos de uso são coletados. Logs ficam localmente no diretório de dados do app e podem ser removidos pelo usuário.
