using SteamBacklogPicker.UI.Services.Localization;

namespace SteamBacklogPicker.UI.Tests.Fakes;

internal sealed class FakeLocalizationService : ILocalizationService
{
    public event EventHandler? LanguageChanged
    {
        add { }
        remove { }
    }

    public event EventHandler<IReadOnlyDictionary<string, string>>? ResourcesChanged
    {
        add { }
        remove { }
    }

    public string CurrentLanguage => "en";

    public IReadOnlyList<string> SupportedLanguages => new[] { "en" };

    public void SetLanguage(string languageCode)
    {
    }

    public string GetString(string key) => key;

    public string GetString(string key, params object[] arguments) => string.Format(key, arguments);

    public string FormatGameCount(int count) => count.ToString();

    public IReadOnlyDictionary<string, string> GetAllStrings() => new Dictionary<string, string>();
}
