using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Reactive;

namespace VVO.UI;

public static class IconColors
{
    /// <summary>
    /// The colours offered as swatches. Mid tones, so an icon in any of them reads on the dark
    /// themes and the light ones alike.
    /// </summary>
    public static IReadOnlyList<Color> Presets { get; } =
    [
        Color.Parse("#E35D5D"),
        Color.Parse("#E8893C"),
        Color.Parse("#D9A82B"),
        Color.Parse("#4CAF6E"),
        Color.Parse("#2BB3A3"),
        Color.Parse("#3D8FE0"),
        Color.Parse("#7B6CF0"),
        Color.Parse("#B06AD9"),
        Color.Parse("#E0609A")
    ];

    /// <summary>
    /// The shade the application's own icons are drawn in, which a virtual volume follows
    /// until the user picks a colour of its own.
    /// </summary>
    public static Color Default
    {
        get
        {
            var application = Application.Current;

            // Theme brushes are keyed per variant, so looking one up without naming the
            // variant in force finds the wrong shade or none at all.
            return application != null
                   && application.TryFindResource(
                       "IconBrush", application.ActualThemeVariant, out var resource)
                   && resource is ISolidColorBrush brush
                ? brush.Color
                : Colors.Gray;
        }
    }

    // Held per application: each headless test runs under one of its own
    private static Application? _defaultOwner;
    private static SolidColorBrush? _defaultBrush;

    /// <summary>
    /// The default shade as one brush that keeps up with the theme, so an icon left in it
    /// changes along with the theme instead of keeping the shade it was first drawn in.
    /// </summary>
    public static IBrush DefaultBrush
    {
        get
        {
            var application = Application.Current;
            if (_defaultBrush == null || _defaultOwner != application)
            {
                _defaultOwner = application;
                _defaultBrush = Following(application);
            }

            return _defaultBrush;
        }
    }

    private static SolidColorBrush Following(Application? application)
    {
        var brush = new SolidColorBrush(Default);
        application?.GetResourceObservable("IconBrush")
            .Subscribe(new AnonymousObserver<object?>(_ => brush.Color = Default));

        return brush;
    }

    public static IBrush Brush(string? color)
    {
        return !string.IsNullOrWhiteSpace(color) && Color.TryParse(color, out var parsed)
            ? new SolidColorBrush(parsed)
            : DefaultBrush;
    }
}
