using System;
using System.Collections.Generic;

namespace SteamBacklogPicker.UI.Services.Localization;

public interface ILocalizationService
{
    event EventHandler? LanguageChanged;

    event EventHandler<IReadOnlyDictionary<string, string>>? ResourcesChanged;

    string CurrentLanguage { get; }

    IReadOnlyList<string> SupportedLanguages { get; }

    void SetLanguage(string languageCode);

    string GetString(string key);

    string GetString(string key, params object[] arguments);

    string FormatGameCount(int count);

    IReadOnlyDictionary<string, string> GetAllStrings();
}
