using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace SteamBacklogPicker.UI.Services;

public sealed class LocalizationService : ILocalizationService
{
    private static readonly IReadOnlyDictionary<string, string> Portuguese = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Filters_PanelAutomationName"] = "Painel de filtros",
        ["Filters_Title"] = "Filtros",
        ["Filters_RequireInstalled"] = "Somente instalados",
        ["Filters_ExcludeDeckUnsupported"] = "Excluir incompatíveis com o Steam Deck",
        ["Filters_ContentTypesLabel"] = "TIPOS DE CONTEÚDO",
        ["Filters_IncludeGames"] = "Jogos",
        ["Filters_IncludeSoundtracks"] = "Trilhas sonoras",
        ["Filters_IncludeSoftware"] = "Softwares",
        ["Filters_IncludeTools"] = "Ferramentas",
        ["Filters_IncludeVideos"] = "Vídeos",
        ["Filters_IncludeOther"] = "Outros conteúdos",
        ["Filters_StatusLabel"] = "STATUS",
        ["Filters_TimeLimitsLabel"] = "LIMITES DE TEMPO",
        ["Filters_MaxPlaytimeLabel"] = "Máx. horas jogadas",
        ["Filters_MaxSessionLabel"] = "Máx. sessão alvo",
        ["Filters_MaxCompletionLabel"] = "Máx. conclusão estimada",
        ["Filters_RecentExclusionLabel"] = "SORTEIOS RECENTES",
        ["Filters_RecentExclusion_HelpText"] = "Quantidade de jogos sorteados recentemente que serão ignorados nas próximas tentativas",
        ["Filters_RecentExclusion_ValuePrefix"] = "Ignorar últimos:",
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
        ["Status_FilteredCount"] = "{0} disponíveis após aplicar os filtros (de {1}).",
        ["GameDetails_PanelAutomationName"] = "Painel de detalhes do jogo",
        ["GameDetails_NextGameTitle"] = "Próximo jogo",
        ["GameDetails_SelectedTitleAutomation"] = "Título do jogo selecionado",
        ["GameDetails_InstallationAutomation"] = "Estado de instalação do jogo",
        ["GameDetails_NoSelectionTitle"] = "Nenhum jogo selecionado",
        ["GameDetails_NoCoverTitle"] = "Não encontramos a capa deste jogo ainda.",
        ["GameDetails_NoCoverSubtitle"] = "Não encontramos a capa deste jogo ainda.",
        ["GameDetails_DrawPrompt"] = "Use o botão Sortear para descobrir o próximo jogo da fila.",
        ["GameDetails_PlayButton"] = "Jogar",
        ["GameDetails_PlayButton_Automation"] = "Abrir jogo no Steam",
        ["GameDetails_InstallButton"] = "Instalar",
        ["GameDetails_InstallButton_Automation"] = "Instalar jogo no Steam",
        ["GameDetails_DrawingOverlay"] = "Sorteando...",
        ["GameDetails_InstallState_Installed"] = "Instalado",
        ["GameDetails_InstallState_Available"] = "Disponível para instalar",
        ["GameDetails_InstallState_FamilySharing"] = "Disponível via compartilhamento familiar",
        ["GameDetails_InstallState_Unknown"] = "Estado de instalação desconhecido",
        ["GameDetails_BacklogSectionTitle"] = "PLANEJAMENTO",
        ["GameDetails_StatusAutomation"] = "Alterar status do backlog",
        ["GameDetails_PersonalNotesLabel"] = "ANOTAÇÕES PESSOAIS",
        ["GameDetails_PersonalNotesAutomation"] = "Campo de anotações pessoais",
        ["GameDetails_PlaytimeLabel"] = "Horas jogadas (h)",
        ["GameDetails_PlaytimeAutomation"] = "Horas jogadas registradas",
        ["GameDetails_TargetSessionLabel"] = "Sessão alvo (h)",
        ["GameDetails_TargetSessionAutomation"] = "Duração alvo da próxima sessão",
        ["GameDetails_EstimatedCompletion"] = "Conclusão estimada: {0} h",
        ["GameDetails_EstimatedCompletionUnavailable"] = "Conclusão estimada indisponível",
        ["GameDetails_ProgressWithEstimate"] = "Progresso: {0} h de {1} h ({2}% concluído)",
        ["GameDetails_ProgressPlaytimeOnly"] = "Horas registradas: {0} h",
        ["GameDetails_ProgressUnavailable"] = "Sem dados de progresso",
        ["GameDetails_SaveButton"] = "Salvar dados",
        ["GameDetails_SaveButton_Automation"] = "Salvar dados personalizados do jogo",
        ["GameDetails_InvalidHours"] = "Insira um número válido de horas.",
        ["Notifications_GameDrawn"] = "Jogo sorteado!",
        ["Filters_NoCollection"] = "Nenhuma coleção",
        ["Language_Portuguese"] = "Português",
        ["Language_English"] = "Inglês",
        ["Common_GameCount_Singular"] = "{0} jogo",
        ["Common_GameCount_Plural"] = "{0} jogos",
        ["BacklogStatus_Unspecified"] = "Sem status",
        ["BacklogStatus_Wishlist"] = "Lista de desejos",
        ["BacklogStatus_Backlog"] = "Backlog",
        ["BacklogStatus_Playing"] = "Jogando",
        ["BacklogStatus_Completed"] = "Concluído",
        ["BacklogStatus_Abandoned"] = "Abandonado",
    };

    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Filters_PanelAutomationName"] = "Filter panel",
        ["Filters_Title"] = "Filters",
        ["Filters_RequireInstalled"] = "Only installed",
        ["Filters_ExcludeDeckUnsupported"] = "Exclude Steam Deck incompatible",
        ["Filters_ContentTypesLabel"] = "CONTENT TYPES",
        ["Filters_IncludeGames"] = "Games",
        ["Filters_IncludeSoundtracks"] = "Soundtracks",
        ["Filters_IncludeSoftware"] = "Software",
        ["Filters_IncludeTools"] = "Tools",
        ["Filters_IncludeVideos"] = "Videos",
        ["Filters_IncludeOther"] = "Other content",
        ["Filters_StatusLabel"] = "STATUS",
        ["Filters_TimeLimitsLabel"] = "TIME LIMITS",
        ["Filters_MaxPlaytimeLabel"] = "Max playtime",
        ["Filters_MaxSessionLabel"] = "Max target session",
        ["Filters_MaxCompletionLabel"] = "Max estimated completion",
        ["Filters_RecentExclusionLabel"] = "RECENT DRAWS",
        ["Filters_RecentExclusion_HelpText"] = "Number of recently drawn games to skip when picking the next game",
        ["Filters_RecentExclusion_ValuePrefix"] = "Exclude last:",
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
        ["Status_FilteredCount"] = "{0} available after applying filters (of {1}).",
        ["GameDetails_PanelAutomationName"] = "Game details panel",
        ["GameDetails_NextGameTitle"] = "Next game",
        ["GameDetails_SelectedTitleAutomation"] = "Selected game title",
        ["GameDetails_InstallationAutomation"] = "Game installation status",
        ["GameDetails_NoSelectionTitle"] = "No game selected",
        ["GameDetails_NoCoverTitle"] = "We haven't found artwork for this game yet.",
        ["GameDetails_NoCoverSubtitle"] = "We haven't found artwork for this game yet.",
        ["GameDetails_DrawPrompt"] = "Use the Draw button to discover the next game in your queue.",
        ["GameDetails_PlayButton"] = "Play",
        ["GameDetails_PlayButton_Automation"] = "Launch game in Steam",
        ["GameDetails_InstallButton"] = "Install",
        ["GameDetails_InstallButton_Automation"] = "Install game in Steam",
        ["GameDetails_DrawingOverlay"] = "Drawing...",
        ["GameDetails_InstallState_Installed"] = "Installed",
        ["GameDetails_InstallState_Available"] = "Available to install",
        ["GameDetails_InstallState_FamilySharing"] = "Available via family sharing",
        ["GameDetails_InstallState_Unknown"] = "Installation status unknown",
        ["GameDetails_BacklogSectionTitle"] = "BACKLOG PLAN",
        ["GameDetails_StatusAutomation"] = "Change backlog status",
        ["GameDetails_PersonalNotesLabel"] = "PERSONAL NOTES",
        ["GameDetails_PersonalNotesAutomation"] = "Personal notes field",
        ["GameDetails_PlaytimeLabel"] = "Playtime (h)",
        ["GameDetails_PlaytimeAutomation"] = "Logged playtime",
        ["GameDetails_TargetSessionLabel"] = "Target session (h)",
        ["GameDetails_TargetSessionAutomation"] = "Target length for next session",
        ["GameDetails_EstimatedCompletion"] = "Estimated completion: {0} h",
        ["GameDetails_EstimatedCompletionUnavailable"] = "Estimated completion unavailable",
        ["GameDetails_ProgressWithEstimate"] = "Progress: {0} h of {1} h ({2}% complete)",
        ["GameDetails_ProgressPlaytimeOnly"] = "Logged playtime: {0} h",
        ["GameDetails_ProgressUnavailable"] = "Progress data unavailable",
        ["GameDetails_SaveButton"] = "Save details",
        ["GameDetails_SaveButton_Automation"] = "Save custom game data",
        ["GameDetails_InvalidHours"] = "Enter a valid number of hours.",
        ["Notifications_GameDrawn"] = "Game drawn!",
        ["Filters_NoCollection"] = "No collection",
        ["Language_Portuguese"] = "Portuguese",
        ["Language_English"] = "English",
        ["Common_GameCount_Singular"] = "{0} game",
        ["Common_GameCount_Plural"] = "{0} games",
        ["BacklogStatus_Unspecified"] = "No status",
        ["BacklogStatus_Wishlist"] = "Wishlist",
        ["BacklogStatus_Backlog"] = "Backlog",
        ["BacklogStatus_Playing"] = "Playing",
        ["BacklogStatus_Completed"] = "Completed",
        ["BacklogStatus_Abandoned"] = "Abandoned",
    };

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _translations;
    private readonly IReadOnlyList<string> _supportedLanguages;
    private string _currentLanguage = "pt-BR";

    public event EventHandler? LanguageChanged;

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

    private void ApplyLanguage(string languageCode)
    {
        if (!_translations.TryGetValue(languageCode, out var resources))
        {
            return;
        }

        _currentLanguage = languageCode;

        if (Application.Current is { } app)
        {
            foreach (var (key, value) in resources)
            {
                app.Resources[key] = value;
            }
        }

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
