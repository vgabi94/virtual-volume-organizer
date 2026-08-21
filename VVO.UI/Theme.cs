using System;
using Avalonia;
using Avalonia.Styling;

namespace VVO.UI;

/// <summary>
/// Which of the two themes the application is wearing. Both are defined for every colour it
/// uses, so changing this repaints what is already on screen rather than needing a restart.
/// </summary>
public static class Theme
{
    /// <summary>
    /// Takes the theme in place of the application. A test session is one application shared by
    /// every test in the assembly, so repainting it for real would outlive the test that asked.
    /// </summary>
    public static Action<bool>? Applying { get; set; }

    public static void Reset() => Applying = null;

    public static void Apply(bool dark)
    {
        if (Applying != null)
        {
            Applying(dark);
            return;
        }

        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }
}
