using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using SteamBacklogPicker.UI.Services.Localization;

[assembly: AvaloniaTestApplication(typeof(SteamBacklogPicker.Linux.Tests.HeadlessTestAppBuilder))]

namespace SteamBacklogPicker.Linux.Tests;

public static class HeadlessTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<HeadlessTestApplication>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class HeadlessTestApplication : Avalonia.Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://SteamBacklogPicker.Linux/"))
        {
            Source = new Uri("avares://SteamBacklogPicker.Linux/Styles/AppStyles.axaml")
        });
        var localization = new LocalizationService();
        localization.SetLanguage("en-US");
        foreach (var (key, value) in localization.GetAllStrings()) Resources[key] = value;
    }
}
