using System.Globalization;
using System.Windows.Data;

namespace ClearTool.App.Converters;

public sealed class BytesToHumanReadableConverter : IValueConverter
{
    public static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long bytes ? Format(bytes) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
