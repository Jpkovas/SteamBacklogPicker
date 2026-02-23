using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SteamBacklogPicker.Linux.Views.Converters;

public sealed class StringNullOrWhiteSpaceToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is string mode && string.Equals(mode, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(value as string);
        }

        return string.IsNullOrWhiteSpace(value as string);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
