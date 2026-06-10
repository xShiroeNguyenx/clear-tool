using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace ClearTool.App.Services;

/// <summary>
/// Đổi màu palette Safe/Caution/Keep theo theme sáng/tối.
/// Mutate trực tiếp .Color của brush trong Application.Resources (brush KHÔNG
/// freeze) — mọi chỗ đang giữ tham chiếu brush tự đổi màu, không cần rebind.
/// </summary>
public static class SafetyPalette
{
    private static readonly (string Key, Color Light, Color Dark)[] Palette =
    [
        ("SafeBrush",         Rgb(0x2E, 0x7D, 0x32), Rgb(0x66, 0xBB, 0x6A)),
        ("SafeLightBrush",    Rgb(0xE8, 0xF5, 0xE9), Argb(0x2A, 0x66, 0xBB, 0x6A)),
        ("CautionBrush",      Rgb(0xF9, 0xA8, 0x25), Rgb(0xFF, 0xCA, 0x28)),
        ("CautionLightBrush", Rgb(0xFF, 0xF8, 0xE1), Argb(0x2A, 0xFF, 0xCA, 0x28)),
        ("KeepBrush",         Rgb(0xC6, 0x28, 0x28), Rgb(0xEF, 0x53, 0x50)),
        ("KeepLightBrush",    Rgb(0xFF, 0xEB, 0xEE), Argb(0x2A, 0xEF, 0x53, 0x50)),
        ("UnknownBrush",      Rgb(0x75, 0x75, 0x75), Rgb(0x9E, 0x9E, 0x9E)),
    ];

    public static void Apply(ApplicationTheme theme)
    {
        bool dark = theme == ApplicationTheme.Dark;
        foreach (var (key, light, darkColor) in Palette)
        {
            if (Application.Current.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
                brush.Color = dark ? darkColor : light;
        }
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    private static Color Argb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
}
