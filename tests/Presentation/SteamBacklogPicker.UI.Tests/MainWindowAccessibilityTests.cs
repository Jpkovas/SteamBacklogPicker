using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
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
            .FirstOrDefault(element => string.Equals(
                (string?)element.Attribute(XName.Get("AutomationProperties.Name")),
                "Selecionar coleção",
                StringComparison.Ordinal));

        comboBox.Should().NotBeNull();
        comboBox!.Attribute(XName.Get("AutomationProperties.HelpText"))
            ?.Value.Should().Contain("coleção", "o menu deve orientar o usuário sobre sua função");
    }
}
