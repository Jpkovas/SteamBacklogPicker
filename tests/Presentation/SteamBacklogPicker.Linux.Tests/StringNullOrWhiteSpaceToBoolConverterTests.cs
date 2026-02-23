using System;
using System.Globalization;
using FluentAssertions;
using Xunit;
using SteamBacklogPicker.Linux.Views.Converters;

namespace SteamBacklogPicker.Linux.Tests;

public sealed class StringNullOrWhiteSpaceToBoolConverterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Fact]
    public void Convert_ShouldReturnTrue_WhenValueIsNullOrWhitespace()
    {
        var converter = new StringNullOrWhiteSpaceToBoolConverter();

        converter.Convert(null, typeof(bool), null, Culture).Should().Be(true);
        converter.Convert(string.Empty, typeof(bool), null, Culture).Should().Be(true);
        converter.Convert("   ", typeof(bool), null, Culture).Should().Be(true);
    }

    [Fact]
    public void Convert_ShouldReturnFalse_WhenValueHasText()
    {
        var converter = new StringNullOrWhiteSpaceToBoolConverter();

        converter.Convert("cover.png", typeof(bool), null, Culture).Should().Be(false);
    }

    [Fact]
    public void Convert_ShouldInvertResult_WhenInvertParameterIsProvided()
    {
        var converter = new StringNullOrWhiteSpaceToBoolConverter();

        converter.Convert("cover.png", typeof(bool), "Invert", Culture).Should().Be(true);
        converter.Convert(null, typeof(bool), "Invert", Culture).Should().Be(false);
    }

    [Fact]
    public void ConvertBack_ShouldThrowNotSupportedException()
    {
        var converter = new StringNullOrWhiteSpaceToBoolConverter();

        var action = () => converter.ConvertBack(true, typeof(string), null, Culture);

        action.Should().Throw<NotSupportedException>();
    }
}
