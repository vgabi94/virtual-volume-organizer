using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
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

    private static readonly IReadOnlyList<WindowTransparencyLevel> OverMica =
        [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None];

    /// <summary>
    /// What a window asks the system to draw behind it: Mica under a Mica theme, falling back to
    /// acrylic and then to nothing, and nothing at all otherwise.
    /// </summary>
    public static readonly IValueConverter TransparencyFor =
        new FuncValueConverter<ThemeVariant?, IReadOnlyList<WindowTransparencyLevel>>(variant =>
            IsMica(variant) ? OverMica : []);

    public static bool IsMica(ThemeVariant? variant) => variant == MicaDark || variant == MicaLight;

    public const string OpaqueClass = "Opaque";

    private static bool _tracking;

    /// <summary>
    /// Marks every window the system draws no backdrop behind, now and whenever that changes, so
    /// its styles can paint it solid rather than leave it clear over nothing.
    /// </summary>
    public static void Track()
    {
        if (_tracking)
            return;

        _tracking = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => Settle(window), RoutingStrategies.Direct);
        TopLevel.ActualTransparencyLevelProperty.Changed.AddClassHandler<Window>((window, _) => Settle(window));
    }

    private static void Settle(Window window)
    {
        window.Classes.Set(OpaqueClass, window.ActualTransparencyLevel == WindowTransparencyLevel.None);
    }

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
