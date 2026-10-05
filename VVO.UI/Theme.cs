using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Styling;

namespace VVO.UI;

/// <summary>A colour theme as Options offers it: the name shown and the variant it puts on.</summary>
public sealed record ColourTheme(string Name, ThemeVariant Variant)
{
    // What the settings file stores, which outlives a renamed entry in the list
    public string Key => (string)Variant.Key;

    public override string ToString() => Name;
}

/// <summary>
/// Which colour theme the application is wearing. Every colour is defined for each of them, so
/// changing this repaints what is already on screen rather than needing a restart.
/// </summary>
public static class Theme
{
    // Each one inherits Dark or Light, so it only has to name the colours it changes
    public static readonly ThemeVariant SlateDark = new("SlateDark", ThemeVariant.Dark);
    public static readonly ThemeVariant SlateLight = new("SlateLight", ThemeVariant.Light);

    /// <summary>Every colour theme, in the order Options lists them. The first is the default.</summary>
    public static IReadOnlyList<ColourTheme> All { get; } =
    [
        new("Dark", ThemeVariant.Dark),
        new("Light", ThemeVariant.Light),
        new("Slate Dark", SlateDark),
        new("Slate Light", SlateLight)
    ];

    public static ColourTheme Default => All[0];

    // A key that is not on the list, from a hand-edited file or a theme since dropped, opens on
    // the default rather than on nothing
    public static ColourTheme Named(string? key) => All.FirstOrDefault(theme => theme.Key == key) ?? Default;

    /// <summary>What a settings file from before the list meant by its dark and Slate flags.</summary>
    public static string KeyFor(bool dark, bool slate) => (string)((dark, slate) switch
    {
        (true, true) => SlateDark,
        (false, true) => SlateLight,
        (true, false) => ThemeVariant.Dark,
        _ => ThemeVariant.Light
    }).Key;

    /// <summary>
    /// Takes the theme in place of the application. A test session is one application shared by
    /// every test in the assembly, so repainting it for real would outlive the test that asked.
    /// </summary>
    public static Action<ColourTheme>? Applying { get; set; }

    public static void Reset() => Applying = null;

    public static void Apply(ColourTheme theme)
    {
        if (Applying != null)
        {
            Applying(theme);
            return;
        }

        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = theme.Variant;
        }
    }
}
