using System;
using Avalonia;
using Avalonia.Styling;

namespace VVO.UI;

/// <summary>
/// Which of the four themes the application is wearing: dark or light, each plain or over Mica.
/// Every colour is defined for each of them, so changing this repaints what is already on screen
/// rather than needing a restart.
/// </summary>
public static class Theme
{
    // Each Mica variant inherits its plain one, so it only has to name the colours it changes
    public static readonly ThemeVariant MicaDark = new("MicaDark", ThemeVariant.Dark);
    public static readonly ThemeVariant MicaLight = new("MicaLight", ThemeVariant.Light);

    /// <summary>
    /// Takes the theme in place of the application. A test session is one application shared by
    /// every test in the assembly, so repainting it for real would outlive the test that asked.
    /// </summary>
    public static Action<bool, bool>? Applying { get; set; }

    public static void Reset() => Applying = null;

    public static ThemeVariant VariantFor(bool dark, bool mica) => (dark, mica) switch
    {
        (true, true) => MicaDark,
        (false, true) => MicaLight,
        (true, false) => ThemeVariant.Dark,
        _ => ThemeVariant.Light
    };

    public static void Apply(bool dark, bool mica)
    {
        if (Applying != null)
        {
            Applying(dark, mica);
            return;
        }

        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = VariantFor(dark, mica);
        }
    }
}
