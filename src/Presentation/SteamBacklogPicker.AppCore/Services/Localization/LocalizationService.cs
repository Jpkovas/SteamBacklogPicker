using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SteamBacklogPicker.UI.Services.Localization;

public sealed class LocalizationService : ILocalizationService
{
    private static readonly IReadOnlyDictionary<string, string> Portuguese = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Header_Tagline"] = "Descubra o próximo jogo da sua fila",
        ["Workspace_discover"] = "Descobrir",
        ["Workspace_library"] = "Biblioteca",
        ["Workspace_history"] = "Histórico",
        ["Workspace_settings"] = "Configurações",
        ["Workspace_discover_Subtitle"] = "Seu próximo jogo já está na sua biblioteca.",
        ["Workspace_library_Subtitle"] = "Explore seus jogos. Encontre o que combina com hoje.",
        ["Workspace_history_Subtitle"] = "Reencontre suas escolhas e continue de onde parou.",
        ["Workspace_settings_Subtitle"] = "Sua conta, suas fontes e suas preferências.",
        ["Workspace_Pick"] = "Escolher para mim",
        ["Workspace_Next"] = "Outra opção",
        ["Workspace_Explore"] = "Explorar biblioteca",
        ["Workspace_Search"] = "Buscar jogo ou App ID",
        ["Workspace_All"] = "Todos",
        ["Workspace_Owned"] = "Próprios",
        ["Workspace_Family"] = "Da família",
        ["Workspace_AccessUnknown"] = "Acesso não confirmado",
        ["Workspace_Installed"] = "Instalados",
        ["Workspace_Suggestions"] = "Sugestões",
        ["Workspace_Advanced"] = "Mais filtros",
        ["Workspace_ClearFilters"] = "Limpar filtros",
        ["Workspace_NoMatches"] = "Nenhum jogo por aqui.",
        ["Workspace_NoMatchesHelp"] = "Tente outra busca ou limpe os filtros para explorar a biblioteca.",
        ["Workspace_NoLibrary"] = "Sua biblioteca começa aqui.",
        ["Workspace_NoLibraryHelp"] = "Abra o Steam e sincronize sua biblioteca. Conecte sua conta para consultar jogos da família.",
        ["Workspace_CountSummary"] = "{0} de {1} jogos",
        ["Workspace_Ready"] = "PARA JOGAR HOJE",
        ["Workspace_ReasonInstalled"] = "Instalado neste computador",
        ["Workspace_ReasonFamily"] = "Acesso pela família · disponibilidade a confirmar no Steam",
        ["Workspace_ReasonOwned"] = "Da sua biblioteca pessoal",
        ["Workspace_ReasonLocal"] = "Encontrado nos arquivos locais do Steam",
        ["Workspace_LocalAccount"] = "Conta local",
        ["Workspace_Account"] = "Conta Steam",
        ["Workspace_LocalMode"] = "Biblioteca local",
        ["Workspace_LocalCoverage"] = "Cobertura local · pode haver jogos ausentes",
        ["Workspace_Updated"] = "Atualizado às",
        ["Workspace_Sync"] = "Sincronizar",
        ["Workspace_Canceled"] = "Sincronização cancelada.",
        ["Workspace_RefreshFailed"] = "Não foi possível atualizar. A última biblioteca foi preservada.",
        ["Workspace_BacklogSaved"] = "Preferência salva. Você pode desfazer.",
        ["Workspace_Backlog"] = "SEU BACKLOG",
        ["Workspace_Active"] = "Para jogar",
        ["Workspace_AllStates"] = "Todos os estados",
        ["Backlog_None"] = "Sem classificação",
        ["Backlog_WantToPlay"] = "Quero jogar",
        ["Backlog_Playing"] = "Jogando",
        ["Backlog_Completed"] = "Concluído",
        ["Backlog_Hidden"] = "Oculto",
        ["Backlog_Later"] = "Mais tarde",
        ["Workspace_Undo"] = "Desfazer",
        ["Workspace_NoHistory"] = "Suas próximas escolhas vão aparecer aqui.",
        ["Workspace_ClearHistory"] = "Limpar histórico",
        ["Workspace_Connect"] = "Conectar ao Steam",
        ["Workspace_Disconnect"] = "Desconectar",
        ["Workspace_Cancel"] = "Cancelar",
        ["Workspace_FamilyTitle"] = "Traga sua família para a biblioteca.",
        ["Workspace_FamilyHelp"] = "Conecte com o QR do Steam para consultar jogos compartilhados. A sessão fica somente em memória; sua senha não é armazenada.",
        ["Workspace_QrHelp"] = "Leia este QR com o aplicativo Steam no celular e aprove a conexão.",
        ["Workspace_Network"] = "Atualizar nomes e capas pela internet",
        ["Workspace_NetworkHelp"] = "O cache é do Backlog Picker. Desative para usar somente dados locais. Os arquivos do Steam não são alterados.",
        ["Workspace_AvoidRepeats"] = "Evitar repetições até percorrer os candidatos",
        ["Workspace_Diagnostics"] = "Fontes e sincronização",
        ["Workspace_MissingNames"] = "Títulos a completar",
        ["Workspace_About"] = "Feito para jogar mais e escolher menos.",
        ["Workspace_Privacy"] = "Dados locais e privacidade",
        ["Workspace_Language"] = "Idioma",
        ["Workspace_Connection"] = "STEAM FAMILY",
        ["Workspace_NotConnected"] = "Não conectado",
        ["Workspace_Connected"] = "Conectado",
        ["Workspace_Total"] = "Jogos conhecidos",
        ["Workspace_FilterHelp"] = "Filtros avançados",
        ["Workspace_InstallUnknown"] = "O Steam confirma a licença ao abrir ou instalar.",
        ["Filters_PanelAutomationName"] = "Painel de filtros",
        ["Filters_Title"] = "Filtros",
        ["Filters_RequireInstalled"] = "Somente instalados",
        ["Filters_ExcludeDeckUnsupported"] = "Excluir incompatíveis com o Steam Deck",
        ["Filters_RequireMacCompatible"] = "Somente compatíveis com macOS",
        ["Filters_ContentTypesLabel"] = "TIPOS DE CONTEÚDO",
        ["Filters_IncludeGames"] = "Jogos",
        ["Filters_IncludeSoundtracks"] = "Trilhas sonoras",
        ["Filters_IncludeSoftware"] = "Softwares",
        ["Filters_IncludeTools"] = "Ferramentas",
        ["Filters_IncludeVideos"] = "Vídeos",
        ["Filters_IncludeOther"] = "Outros conteúdos",
        ["Filters_StorefrontsLabel"] = "LOJAS",
        ["Filters_IncludeSteam"] = "Steam",
        ["Filters_CollectionLabel"] = "COLEÇÃO",
        ["Filters_SelectCollection"] = "Selecionar coleção",
        ["Filters_SelectCollection_HelpText"] = "Escolha uma coleção personalizada para filtrar",
        ["Filters_RefreshButton"] = "Atualizar biblioteca",
        ["Filters_RefreshButton_Automation"] = "Atualizar biblioteca",
        ["Filters_DrawButton"] = "Sortear",
        ["Filters_DrawButton_Automation"] = "Sortear jogo",
        ["Filters_DrawButton_HelpText"] = "Seleciona um jogo aleatório aplicando os filtros",
        ["Status_LoadingLibrary"] = "Carregando biblioteca...",
        ["Status_Drawing"] = "Sorteando...",
        ["Status_Drawn"] = "Jogo sorteado: {0}",
        ["Status_NoGamesFound"] = "Nenhum jogo encontrado nos diretórios configurados.",
        ["Status_NoMatches"] = "Nenhum jogo corresponde aos filtros atuais (0 de {0}).",
        ["Status_AllEligible"] = "{0} disponíveis para sorteio.",
        ["Status_AllEligible_Singular"] = "{0} disponível para sorteio.",
        ["Status_AllEligible_Plural"] = "{0} disponíveis para sorteio.",
        ["Status_FilteredCount"] = "{0} disponíveis após aplicar os filtros (de {1}).",
        ["Status_FilteredCount_Singular"] = "{0} disponível após aplicar os filtros (de {1}).",
        ["Status_FilteredCount_Plural"] = "{0} disponíveis após aplicar os filtros (de {1}).",
        ["Status_Title"] = "Status",
        ["GameDetails_PanelAutomationName"] = "Painel de detalhes do jogo",
        ["GameDetails_NextGameTitle"] = "Próximo jogo",
        ["GameDetails_StorefrontLabel"] = "Loja",
        ["GameDetails_ArtworkLabel"] = "Arte",
        ["GameDetails_MetadataLabel"] = "Detalhes",
        ["GameDetails_SelectedTitleAutomation"] = "Título do jogo selecionado",
        ["GameDetails_InstallationAutomation"] = "Estado de instalação do jogo",
        ["GameDetails_NoSelectionTitle"] = "Nenhum jogo selecionado",
        ["GameDetails_NoCoverTitle"] = "Sem capa local disponível",
        ["GameDetails_NoCoverSubtitle"] = "O jogo foi carregado, mas nenhuma arte local foi encontrada.",
        ["GameDetails_DrawPrompt"] = "Use o botão Sortear para descobrir o próximo jogo da fila.",
        ["GameDetails_PlayButton"] = "Jogar",
        ["GameDetails_PlayButton_Automation"] = "Abrir jogo no Steam",
        ["GameDetails_InstallButton"] = "Instalar",
        ["GameDetails_InstallButton_Automation"] = "Instalar jogo no Steam",
        ["GameDetails_DrawingOverlay"] = "Sorteando...",
        ["GameDetails_InstallState_Installed"] = "Instalado",
        ["GameDetails_InstallState_Available"] = "Não instalado",
        ["GameDetails_InstallState_FamilySharing"] = "Disponível via compartilhamento familiar",
        ["GameDetails_InstallState_Unknown"] = "Estado de instalação desconhecido",
        ["Notifications_GameDrawn"] = "Jogo sorteado!",
        ["Filters_NoCollection"] = "Nenhuma coleção",
        ["Storefront_Steam"] = "Steam",
        ["Storefront_Unknown"] = "Origem desconhecida",
        ["Language_Portuguese"] = "Português",
        ["Language_English"] = "Inglês",
        ["Common_GameCount_Singular"] = "{0} jogo",
        ["Common_GameCount_Plural"] = "{0} jogos",
        ["GameLaunch_UnsupportedStorefront"] = "Esta loja ainda não oferece suporte ao lançamento pelo SteamBacklog Picker.",
        ["GameLaunch_LaunchNotInstalled"] = "Instale o jogo antes de executá-lo.",
        ["GameLaunch_SteamMissingAppId"] = "O identificador do aplicativo Steam está ausente para este jogo.",
        ["GameLaunch_SteamAlreadyInstalled"] = "O jogo já está instalado via Steam.",
    };

    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Header_Tagline"] = "Discover the next game in your queue",
        ["Workspace_discover"] = "Discover",
        ["Workspace_library"] = "Library",
        ["Workspace_history"] = "History",
        ["Workspace_settings"] = "Settings",
        ["Workspace_discover_Subtitle"] = "Your next favorite is already in your library.",
        ["Workspace_library_Subtitle"] = "Explore your games. Find something for today.",
        ["Workspace_history_Subtitle"] = "Revisit your picks and find your next adventure.",
        ["Workspace_settings_Subtitle"] = "Your account, sources and preferences.",
        ["Workspace_Pick"] = "Pick something for me",
        ["Workspace_Next"] = "Another pick",
        ["Workspace_Explore"] = "Explore library",
        ["Workspace_Search"] = "Search games or App ID",
        ["Workspace_All"] = "All",
        ["Workspace_Owned"] = "Owned",
        ["Workspace_Family"] = "Family shared",
        ["Workspace_AccessUnknown"] = "Access unconfirmed",
        ["Workspace_Installed"] = "Installed",
        ["Workspace_Suggestions"] = "Suggestions",
        ["Workspace_Advanced"] = "More filters",
        ["Workspace_ClearFilters"] = "Clear filters",
        ["Workspace_NoMatches"] = "No games match yet.",
        ["Workspace_NoMatchesHelp"] = "Try another search or clear the filters to explore your library.",
        ["Workspace_NoLibrary"] = "Your library starts here.",
        ["Workspace_NoLibraryHelp"] = "Open Steam and sync your library. Connect your account to check family games.",
        ["Workspace_CountSummary"] = "{0} of {1} games",
        ["Workspace_Ready"] = "YOUR NEXT ADVENTURE",
        ["Workspace_ReasonInstalled"] = "Installed on this computer",
        ["Workspace_ReasonFamily"] = "Family access · availability to be confirmed by Steam",
        ["Workspace_ReasonOwned"] = "From your owned library",
        ["Workspace_ReasonLocal"] = "Found in your local Steam files",
        ["Workspace_LocalAccount"] = "Local account",
        ["Workspace_Account"] = "Steam account",
        ["Workspace_LocalMode"] = "Local library",
        ["Workspace_LocalCoverage"] = "Local coverage · some games may be missing",
        ["Workspace_Updated"] = "Updated at",
        ["Workspace_Sync"] = "Sync now",
        ["Workspace_Canceled"] = "Synchronization cancelled.",
        ["Workspace_RefreshFailed"] = "Refresh failed. Your previous library has been preserved.",
        ["Workspace_BacklogSaved"] = "Preference saved. You can undo it.",
        ["Workspace_Backlog"] = "YOUR BACKLOG",
        ["Workspace_Active"] = "To play",
        ["Workspace_AllStates"] = "All statuses",
        ["Backlog_None"] = "Unsorted",
        ["Backlog_WantToPlay"] = "Want to play",
        ["Backlog_Playing"] = "Playing",
        ["Backlog_Completed"] = "Completed",
        ["Backlog_Hidden"] = "Hidden",
        ["Backlog_Later"] = "Later",
        ["Workspace_Undo"] = "Undo",
        ["Workspace_NoHistory"] = "Your next picks will appear here.",
        ["Workspace_ClearHistory"] = "Clear history",
        ["Workspace_Connect"] = "Connect to Steam",
        ["Workspace_Disconnect"] = "Disconnect",
        ["Workspace_Cancel"] = "Cancel",
        ["Workspace_FamilyTitle"] = "Bring your family library along.",
        ["Workspace_FamilyHelp"] = "Connect with a Steam QR code to check shared games. The session stays in memory; your password is not stored.",
        ["Workspace_QrHelp"] = "Scan this QR with the Steam mobile app and approve the connection.",
        ["Workspace_Network"] = "Refresh names and artwork online",
        ["Workspace_NetworkHelp"] = "The cache belongs to Backlog Picker. Disable this to use local data only. Steam files are never changed.",
        ["Workspace_AvoidRepeats"] = "Avoid repeats until all candidates have been picked",
        ["Workspace_Diagnostics"] = "Sources and synchronization",
        ["Workspace_MissingNames"] = "Titles to resolve",
        ["Workspace_About"] = "Spend less time choosing. More time playing.",
        ["Workspace_Privacy"] = "Local data and privacy",
        ["Workspace_Language"] = "Language",
        ["Workspace_Connection"] = "STEAM FAMILY",
        ["Workspace_NotConnected"] = "Not connected",
        ["Workspace_Connected"] = "Connected",
        ["Workspace_Total"] = "Known games",
        ["Workspace_FilterHelp"] = "Advanced filters",
        ["Workspace_InstallUnknown"] = "Steam confirms your license when opening or installing.",
        ["Filters_PanelAutomationName"] = "Filter panel",
        ["Filters_Title"] = "Filters",
        ["Filters_RequireInstalled"] = "Only installed",
        ["Filters_ExcludeDeckUnsupported"] = "Exclude Steam Deck incompatible",
        ["Filters_RequireMacCompatible"] = "Only macOS compatible",
        ["Filters_ContentTypesLabel"] = "CONTENT TYPES",
        ["Filters_IncludeGames"] = "Games",
        ["Filters_IncludeSoundtracks"] = "Soundtracks",
        ["Filters_IncludeSoftware"] = "Software",
        ["Filters_IncludeTools"] = "Tools",
        ["Filters_IncludeVideos"] = "Videos",
        ["Filters_IncludeOther"] = "Other content",
        ["Filters_StorefrontsLabel"] = "STORES",
        ["Filters_IncludeSteam"] = "Steam",
        ["Filters_CollectionLabel"] = "COLLECTION",
        ["Filters_SelectCollection"] = "Select collection",
        ["Filters_SelectCollection_HelpText"] = "Choose a custom collection to filter",
        ["Filters_RefreshButton"] = "Refresh library",
        ["Filters_RefreshButton_Automation"] = "Refresh library",
        ["Filters_DrawButton"] = "Draw",
        ["Filters_DrawButton_Automation"] = "Draw game",
        ["Filters_DrawButton_HelpText"] = "Pick a random game using the current filters",
        ["Status_LoadingLibrary"] = "Loading library...",
        ["Status_Drawing"] = "Drawing...",
        ["Status_Drawn"] = "Drawn game: {0}",
        ["Status_NoGamesFound"] = "No games were found in the configured directories.",
        ["Status_NoMatches"] = "No games match the current filters (0 of {0}).",
        ["Status_AllEligible"] = "{0} available to draw.",
        ["Status_AllEligible_Singular"] = "{0} available to draw.",
        ["Status_AllEligible_Plural"] = "{0} available to draw.",
        ["Status_FilteredCount"] = "{0} available after applying filters (of {1}).",
        ["Status_FilteredCount_Singular"] = "{0} available after applying filters (of {1}).",
        ["Status_FilteredCount_Plural"] = "{0} available after applying filters (of {1}).",
        ["Status_Title"] = "Status",
        ["GameDetails_PanelAutomationName"] = "Game details panel",
        ["GameDetails_NextGameTitle"] = "Next game",
        ["GameDetails_StorefrontLabel"] = "Store",
        ["GameDetails_ArtworkLabel"] = "Artwork",
        ["GameDetails_MetadataLabel"] = "Details",
        ["GameDetails_SelectedTitleAutomation"] = "Selected game title",
        ["GameDetails_InstallationAutomation"] = "Game installation status",
        ["GameDetails_NoSelectionTitle"] = "No game selected",
        ["GameDetails_NoCoverTitle"] = "No local artwork available",
        ["GameDetails_NoCoverSubtitle"] = "The game loaded, but no local artwork was found.",
        ["GameDetails_DrawPrompt"] = "Use the Draw button to discover the next game in your queue.",
        ["GameDetails_PlayButton"] = "Play",
        ["GameDetails_PlayButton_Automation"] = "Launch game in Steam",
        ["GameDetails_InstallButton"] = "Install",
        ["GameDetails_InstallButton_Automation"] = "Install game in Steam",
        ["GameDetails_DrawingOverlay"] = "Drawing...",
        ["GameDetails_InstallState_Installed"] = "Installed",
        ["GameDetails_InstallState_Available"] = "Not installed",
        ["GameDetails_InstallState_FamilySharing"] = "Available via family sharing",
        ["GameDetails_InstallState_Unknown"] = "Installation status unknown",
        ["Notifications_GameDrawn"] = "Game drawn!",
        ["Filters_NoCollection"] = "No collection",
        ["Storefront_Steam"] = "Steam",
        ["Storefront_Unknown"] = "Unknown source",
        ["Language_Portuguese"] = "Portuguese",
        ["Language_English"] = "English",
        ["Common_GameCount_Singular"] = "{0} game",
        ["Common_GameCount_Plural"] = "{0} games",
        ["GameLaunch_UnsupportedStorefront"] = "This storefront does not support launching from SteamBacklog Picker yet.",
        ["GameLaunch_LaunchNotInstalled"] = "Install the game before launching it.",
        ["GameLaunch_SteamMissingAppId"] = "The Steam app identifier is missing for this game.",
        ["GameLaunch_SteamAlreadyInstalled"] = "The game is already installed via Steam.",
    };

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _translations;
    private readonly IReadOnlyList<string> _supportedLanguages;
    private string _currentLanguage = "pt-BR";

    public event EventHandler? LanguageChanged;

    public event EventHandler<IReadOnlyDictionary<string, string>>? ResourcesChanged;

    public LocalizationService()
    {
        _translations = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["pt-BR"] = Portuguese,
            ["en-US"] = English,
        };

        _supportedLanguages = _translations.Keys.OrderBy(code => code, StringComparer.OrdinalIgnoreCase).ToArray();

        var preferred = CultureInfo.CurrentUICulture.Name;
        if (!_translations.ContainsKey(preferred))
        {
            preferred = preferred.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? "pt-BR" : "en-US";
        }

        ApplyLanguage(preferred);
    }

    public string CurrentLanguage => _currentLanguage;

    public IReadOnlyList<string> SupportedLanguages => _supportedLanguages;

    public void SetLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return;
        }

        if (string.Equals(languageCode, _currentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_translations.ContainsKey(languageCode))
        {
            return;
        }

        ApplyLanguage(languageCode);
    }

    public string GetString(string key)
    {
        if (TryGetString(_currentLanguage, key, out var value))
        {
            return value;
        }

        var fallback = _currentLanguage.Equals("pt-BR", StringComparison.OrdinalIgnoreCase) ? "en-US" : "pt-BR";
        if (TryGetString(fallback, key, out var fallbackValue))
        {
            return fallbackValue;
        }

        return key;
    }

    public string GetString(string key, params object[] arguments)
    {
        var format = GetString(key);
        return string.Format(GetCultureForCurrentLanguage(), format, arguments);
    }

    public string FormatGameCount(int count)
    {
        var key = count == 1 ? "Common_GameCount_Singular" : "Common_GameCount_Plural";
        return GetString(key, count);
    }

    public IReadOnlyDictionary<string, string> GetAllStrings()
    {
        return _translations.TryGetValue(_currentLanguage, out var resources)
            ? resources
            : _translations["en-US"];
    }

    private void ApplyLanguage(string languageCode)
    {
        if (!_translations.TryGetValue(languageCode, out var resources))
        {
            return;
        }

        _currentLanguage = languageCode;

        ResourcesChanged?.Invoke(this, resources);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool TryGetString(string language, string key, out string value)
    {
        if (_translations.TryGetValue(language, out var resources) &&
            resources.TryGetValue(key, out value!))
        {
            return true;
        }

        value = key;
        return false;
    }

    private CultureInfo GetCultureForCurrentLanguage()
        => CultureInfo.GetCultureInfo(_currentLanguage);
}
