using System.Runtime.ExceptionServices;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using FluentAssertions;
using SteamBacklogPicker.UI;
using Xunit;

namespace SteamBacklogPicker.UI.Tests;

public sealed class VisualMotionTests
{
    [Fact]
    public void LoginQr_ShouldRejectOverCapacityPayloadWithoutBreakingDispatcher()
    {
        RunSta(() =>
        {
            MainWindow.CreateLoginQrImage(new string('a', 4000)).Should().BeNull();
            MainWindow.CreateLoginQrImage("https://s.team/q/fixture")!.IsFrozen.Should().BeTrue();
            MainWindow.CreateLoginQrImage(null).Should().BeNull();
        });
    }

    [Fact]
    public void SearchField_ShouldLeaveEnoughViewportHeightForTypedText()
    {
        RunSta(() =>
        {
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "..", "src", "Presentation",
                "SteamBacklogPicker.UI", "MainWindow.xaml"));
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
            var field = XDocument.Load(path).Descendants()
                .Single(element => (string?)element.Attribute(xaml + "Key") == "Field");
            var textbox = new TextBox
            {
                Style = (Style)XamlReader.Parse(field.ToString()),
                Text = "Among Us",
                Width = 600
            };
            textbox.Measure(new Size(600, 40));
            textbox.Arrange(new Rect(0, 0, 600, 40));
            textbox.UpdateLayout();
            var viewport = (ScrollViewer)textbox.Template.FindName("PART_ContentHost", textbox);

            viewport.ViewportHeight.Should().BeGreaterThanOrEqualTo(textbox.FontSize * 1.2,
                "TextBox forwards its padding to the content host, so extra margins clip typed text");
            viewport.ExtentHeight.Should().BeLessThanOrEqualTo(viewport.ViewportHeight);
        });
    }

    [Fact]
    public void EnabledFeedback_ShouldCloneFrozenTemplateTransformWithoutCrashing()
    {
        RunSta(() =>
        {
            var template = (ControlTemplate)XamlReader.Parse("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
                    <Grid x:Name="FeedbackRoot">
                        <Grid.RenderTransform><TranslateTransform/></Grid.RenderTransform>
                        <Border x:Name="HoverLayer" Opacity="0"/>
                    </Grid>
                </ControlTemplate>
                """);
            var button = new Button { Template = template };
            button.ApplyTemplate();
            var root = (Grid)template.FindName("FeedbackRoot", button);
            var original = new TranslateTransform();
            original.Freeze();
            root.RenderTransform = original;
            VisualMotion.SetFeedback(button, true);

            button.IsEnabled = false;

            root.RenderTransform.Should().NotBeSameAs(original);
            root.RenderTransform.IsFrozen.Should().BeFalse();
            original.Y.Should().Be(0);
            button.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            VisualMotion.SetFeedback(button, false);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(10)).Should().BeTrue();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
