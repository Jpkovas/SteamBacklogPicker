# CHANGES

- Criei o projeto compartilhado `SteamBacklogPicker.AppCore` (net8.0), movendo ViewModels e serviços de UI sem dependência WPF para centralizar fluxos e contratos usados por múltiplos clientes.
- Mantive o cliente WPF como host Windows e adicionei o cliente `SteamBacklogPicker.Linux` (Avalonia) consumindo os mesmos ViewModels/serviços do `AppCore`, além de registrar checklist de paridade em `docs/testing/journey-parity-checklist.md`.
- Substituí o acesso direto ao registry por `ISteamInstallPathProvider` no SteamDiscovery, com providers de Windows (registry) e Linux (STEAM_PATH + heurísticas de diretórios padrão/Flatpak) e integração no `SteamLibraryLocator`.
- Adicionei testes de resolução de path de instalação e cobertura da nova injeção no `SteamLibraryLocator` em `tests/Infrastructure/SteamDiscovery.Tests`.
