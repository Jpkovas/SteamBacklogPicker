import Foundation

enum AppLanguage: String, CaseIterable, Identifiable {
    case portuguese = "pt-BR"
    case english = "en-US"

    var id: String { rawValue }

    static func preferred() -> AppLanguage {
        let code = Locale.current.identifier
        return code.lowercased().hasPrefix("pt") ? .portuguese : .english
    }
}

struct Localization {
    private static let portuguese: [String: String] = [
        "Header_Tagline": "Descubra o próximo jogo da sua fila",
        "Filters_PanelAutomationName": "Painel de filtros",
        "Filters_Title": "Filtros",
        "Filters_RequireInstalled": "Somente instalados",
        "Filters_ExcludeDeckUnsupported": "Excluir incompatíveis com o Steam Deck",
        "Filters_RequireMacCompatible": "Somente compatíveis com macOS",
        "Filters_ContentTypesLabel": "TIPOS DE CONTEÚDO",
        "Filters_IncludeGames": "Jogos",
        "Filters_IncludeSoundtracks": "Trilhas sonoras",
        "Filters_IncludeSoftware": "Softwares",
        "Filters_IncludeTools": "Ferramentas",
        "Filters_IncludeVideos": "Vídeos",
        "Filters_IncludeOther": "Outros conteúdos",
        "Filters_StorefrontsLabel": "LOJAS",
        "Filters_IncludeSteam": "Steam",
        "Filters_CollectionLabel": "COLEÇÃO",
        "Filters_SelectCollection": "Selecionar coleção",
        "Filters_SelectCollection_HelpText": "Escolha uma coleção personalizada para filtrar",
        "Filters_RefreshButton": "Atualizar biblioteca",
        "Filters_RefreshButton_Automation": "Atualizar biblioteca",
        "Filters_DrawButton": "Sortear",
        "Filters_DrawButton_Automation": "Sortear jogo",
        "Filters_DrawButton_HelpText": "Seleciona um jogo aleatório aplicando os filtros",
        "Filters_NoCollection": "Nenhuma coleção",
        "Menu_File": "Arquivo",
        "Menu_Quit": "Encerrar Steam Backlog Picker",
        "Status_LoadingLibrary": "Carregando biblioteca...",
        "Status_Drawing": "Sorteando...",
        "Status_Drawn": "Jogo sorteado: %@",
        "Status_NoGamesFound": "Nenhum jogo encontrado nos diretórios configurados.",
        "Status_NoMatches": "Nenhum jogo corresponde aos filtros atuais (0 de %@).",
        "Status_AllEligible": "%@ disponíveis para sorteio.",
        "Status_AllEligible_Singular": "%@ disponível para sorteio.",
        "Status_AllEligible_Plural": "%@ disponíveis para sorteio.",
        "Status_FilteredCount": "%@ disponíveis após aplicar os filtros (de %@).",
        "Status_FilteredCount_Singular": "%@ disponível após aplicar os filtros (de %@).",
        "Status_FilteredCount_Plural": "%@ disponíveis após aplicar os filtros (de %@).",
        "Status_Title": "Status",
        "Notifications_GameDrawn": "Jogo sorteado!",
        "GameDetails_PanelAutomationName": "Painel de detalhes do jogo",
        "GameDetails_NextGameTitle": "Próximo jogo",
        "GameDetails_StorefrontLabel": "Loja",
        "GameDetails_ArtworkLabel": "Arte",
        "GameDetails_MetadataLabel": "Detalhes",
        "GameDetails_SelectedTitleAutomation": "Título do jogo selecionado",
        "GameDetails_InstallationAutomation": "Estado de instalação do jogo",
        "GameDetails_NoSelectionTitle": "Nenhum jogo selecionado",
        "GameDetails_NoCoverTitle": "Sem capa local disponível",
        "GameDetails_NoCoverSubtitle": "O jogo foi carregado, mas nenhuma arte local foi encontrada.",
        "GameDetails_DrawPrompt": "Use o botão Sortear para descobrir o próximo jogo da fila.",
        "GameDetails_PlayButton": "Jogar",
        "GameDetails_PlayButton_Automation": "Abrir jogo no Steam",
        "GameDetails_InstallButton": "Instalar",
        "GameDetails_InstallButton_Automation": "Instalar jogo no Steam",
        "GameDetails_DrawingOverlay": "Sorteando...",
        "GameDetails_InstallState_Installed": "Instalado",
        "GameDetails_InstallState_Available": "Disponível para instalar",
        "GameDetails_InstallState_FamilySharing": "Disponível via compartilhamento familiar",
        "GameDetails_InstallState_Unknown": "Estado de instalação desconhecido",
        "Storefront_Steam": "Steam",
        "Storefront_Unknown": "Origem desconhecida",
        "Language_Portuguese": "Português",
        "Language_English": "Inglês",
        "Common_GameCount_Singular": "%@ jogo",
        "Common_GameCount_Plural": "%@ jogos",
        "GameLaunch_UnsupportedStorefront": "Esta loja ainda não oferece suporte ao lançamento pelo SteamBacklog Picker.",
        "GameLaunch_LaunchNotInstalled": "Instale o jogo antes de executá-lo.",
        "GameLaunch_SteamMissingAppId": "O identificador do aplicativo Steam está ausente para este jogo.",
        "GameLaunch_SteamAlreadyInstalled": "O jogo já está instalado via Steam.",
        "GameLaunch_OpenFailed": "Não foi possível abrir o link da Steam neste Mac."
    ]

    private static let english: [String: String] = [
        "Header_Tagline": "Discover the next game in your queue",
        "Filters_PanelAutomationName": "Filter panel",
        "Filters_Title": "Filters",
        "Filters_RequireInstalled": "Only installed",
        "Filters_ExcludeDeckUnsupported": "Exclude Steam Deck incompatible",
        "Filters_RequireMacCompatible": "Only macOS compatible",
        "Filters_ContentTypesLabel": "CONTENT TYPES",
        "Filters_IncludeGames": "Games",
        "Filters_IncludeSoundtracks": "Soundtracks",
        "Filters_IncludeSoftware": "Software",
        "Filters_IncludeTools": "Tools",
        "Filters_IncludeVideos": "Videos",
        "Filters_IncludeOther": "Other content",
        "Filters_StorefrontsLabel": "STORES",
        "Filters_IncludeSteam": "Steam",
        "Filters_CollectionLabel": "COLLECTION",
        "Filters_SelectCollection": "Select collection",
        "Filters_SelectCollection_HelpText": "Choose a custom collection to filter",
        "Filters_RefreshButton": "Refresh library",
        "Filters_RefreshButton_Automation": "Refresh library",
        "Filters_DrawButton": "Draw",
        "Filters_DrawButton_Automation": "Draw game",
        "Filters_DrawButton_HelpText": "Pick a random game using the current filters",
        "Filters_NoCollection": "No collection",
        "Menu_File": "File",
        "Menu_Quit": "Quit Steam Backlog Picker",
        "Status_LoadingLibrary": "Loading library...",
        "Status_Drawing": "Drawing...",
        "Status_Drawn": "Drawn game: %@",
        "Status_NoGamesFound": "No games were found in the configured directories.",
        "Status_NoMatches": "No games match the current filters (0 of %@).",
        "Status_AllEligible": "%@ available to draw.",
        "Status_AllEligible_Singular": "%@ available to draw.",
        "Status_AllEligible_Plural": "%@ available to draw.",
        "Status_FilteredCount": "%@ available after applying filters (of %@).",
        "Status_FilteredCount_Singular": "%@ available after applying filters (of %@).",
        "Status_FilteredCount_Plural": "%@ available after applying filters (of %@).",
        "Status_Title": "Status",
        "Notifications_GameDrawn": "Game drawn!",
        "GameDetails_PanelAutomationName": "Game details panel",
        "GameDetails_NextGameTitle": "Next game",
        "GameDetails_StorefrontLabel": "Store",
        "GameDetails_ArtworkLabel": "Artwork",
        "GameDetails_MetadataLabel": "Details",
        "GameDetails_SelectedTitleAutomation": "Selected game title",
        "GameDetails_InstallationAutomation": "Game installation status",
        "GameDetails_NoSelectionTitle": "No game selected",
        "GameDetails_NoCoverTitle": "No local artwork available",
        "GameDetails_NoCoverSubtitle": "The game loaded, but no local artwork was found.",
        "GameDetails_DrawPrompt": "Use the Draw button to discover the next game in your queue.",
        "GameDetails_PlayButton": "Play",
        "GameDetails_PlayButton_Automation": "Launch game in Steam",
        "GameDetails_InstallButton": "Install",
        "GameDetails_InstallButton_Automation": "Install game in Steam",
        "GameDetails_DrawingOverlay": "Drawing...",
        "GameDetails_InstallState_Installed": "Installed",
        "GameDetails_InstallState_Available": "Available to install",
        "GameDetails_InstallState_FamilySharing": "Available via family sharing",
        "GameDetails_InstallState_Unknown": "Installation status unknown",
        "Storefront_Steam": "Steam",
        "Storefront_Unknown": "Unknown source",
        "Language_Portuguese": "Portuguese",
        "Language_English": "English",
        "Common_GameCount_Singular": "%@ game",
        "Common_GameCount_Plural": "%@ games",
        "GameLaunch_UnsupportedStorefront": "This storefront does not support launching from SteamBacklog Picker yet.",
        "GameLaunch_LaunchNotInstalled": "Install the game before launching it.",
        "GameLaunch_SteamMissingAppId": "The Steam app identifier is missing for this game.",
        "GameLaunch_SteamAlreadyInstalled": "The game is already installed via Steam.",
        "GameLaunch_OpenFailed": "Could not open the Steam link on this Mac."
    ]

    static func text(_ key: String, language: AppLanguage) -> String {
        let active = language == .portuguese ? portuguese : english
        let fallback = language == .portuguese ? english : portuguese
        return active[key] ?? fallback[key] ?? key
    }

    static func format(_ key: String, language: AppLanguage, _ arguments: CVarArg...) -> String {
        String(format: text(key, language: language), locale: locale(language), arguments: arguments)
    }

    static func gameCount(_ count: Int, language: AppLanguage) -> String {
        switch language {
        case .portuguese:
            return count == 1 ? "\(count) jogo" : "\(count) jogos"
        case .english:
            return count == 1 ? "\(count) game" : "\(count) games"
        }
    }

    private static func locale(_ language: AppLanguage) -> Locale {
        Locale(identifier: language.rawValue)
    }
}
