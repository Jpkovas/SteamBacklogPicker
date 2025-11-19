using System;
using System.Collections.Generic;

namespace SteamBacklogPicker.UI.Services.Localization;

public interface ILocalizationService
{
    event EventHandler? LanguageChanged;

    string CurrentLanguage { get; }

    IReadOnlyList<string> SupportedLanguages { get; }

    void SetLanguage(string languageCode);

    string GetString(string key);

    string GetString(string key, params object[] arguments);

    string FormatGameCount(int count);
}
