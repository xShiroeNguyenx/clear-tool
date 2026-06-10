using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ClearTool.Core.Rules;

namespace ClearTool.App.Converters;

public sealed class SafetyLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            SafetyLevel.Safe => "SafeBrush",
            SafetyLevel.Caution => "CautionBrush",
            SafetyLevel.Keep => "KeepBrush",
            _ => "UnknownBrush",
        };
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
