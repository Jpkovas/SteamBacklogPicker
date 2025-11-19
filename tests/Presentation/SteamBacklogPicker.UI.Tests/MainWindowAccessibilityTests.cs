using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using SteamBacklogPicker.UI.Services.Localization;
using Xunit;

namespace SteamBacklogPicker.UI.Tests;

public sealed class MainWindowAccessibilityTests
{
    private static readonly XNamespace PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string GetMainWindowPath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(
            baseDirectory,
            "..", "..", "..", "..", "..", "..",
            "src", "Presentation", "SteamBacklogPicker.UI", "MainWindow.xaml"));
    }

    [Fact]
    public void SortearButton_HasAccessibleNameAndDescription()
    {
        var document = XDocument.Load(GetMainWindowPath());
        var drawButton = document
            .Descendants(PresentationNamespace + "Button")
            .FirstOrDefault(element => string.Equals(
                (string?)element.Attribute(XamlNamespace + "Name"),
                "DrawButton",
                StringComparison.Ordinal));

        drawButton.Should().NotBeNull();
        drawButton!.Attribute(XName.Get("AutomationProperties.Name"))
            ?.Value.Should().NotBeNullOrWhiteSpace("o botão de sorteio precisa de um nome acessível");
        drawButton.Attribute(XName.Get("AutomationProperties.HelpText"))
            ?.Value.Should().NotBeNullOrWhiteSpace("o botão de sorteio deve explicar sua ação");
    }

    [Fact]
    public void StatusMessage_AnnouncesChanges()
    {
        var document = XDocument.Load(GetMainWindowPath());
        var statusText = document
            .Descendants(PresentationNamespace + "TextBlock")
            .FirstOrDefault(element => string.Equals(
                (string?)element.Attribute(XName.Get("Text")),
                "{Binding StatusMessage}",
                StringComparison.Ordinal));

        statusText.Should().NotBeNull();
        statusText!.Attribute(XName.Get("AutomationProperties.LiveSetting"))
            ?.Value.Should().Be("Assertive");
    }

    [Fact]
    public void CollectionComboBox_IsAccessible()
    {
        var document = XDocument.Load(GetMainWindowPath());
        var comboBox = document
            .Descendants(PresentationNamespace + "ComboBox")
            .FirstOrDefault(element => element.Attribute(XName.Get("AutomationProperties.Name")) is not null);

        comboBox.Should().NotBeNull("o menu de seleção de coleção precisa expor um nome acessível");

        comboBox!.Attribute(XName.Get("AutomationProperties.Name"))
            ?.Value.Should().Be("{DynamicResource Filters_SelectCollection}",
                "o menu usa recursos dinâmicos para acompanhar a linguagem atual");

        comboBox.Attribute(XName.Get("AutomationProperties.HelpText"))
            ?.Value.Should().Be("{DynamicResource Filters_SelectCollection_HelpText}",
                "o menu deve oferecer uma descrição acessível através de recursos dinâmicos");

        var localization = new LocalizationService();
        localization.SetLanguage("pt-BR");

        localization.GetString("Filters_SelectCollection")
            .Should().Be("Selecionar coleção", "a tradução portuguesa mantém o nome acessível esperado");

        localization.GetString("Filters_SelectCollection_HelpText")
            .Should().Be("Escolha uma coleção personalizada para filtrar",
                "a descrição em português orienta usuários de tecnologia assistiva");
    }
}
