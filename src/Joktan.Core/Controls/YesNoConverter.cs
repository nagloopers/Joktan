using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Joktan.Core.Resources;

namespace Joktan.Core.Controls;

/// <summary>
/// Shows a boolean as "Yes" or "No".
/// </summary>
public sealed class YesNoConverter : IValueConverter
{
    public static readonly YesNoConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Strings.YesNoConverter_Yes : Strings.YesNoConverter_No;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
