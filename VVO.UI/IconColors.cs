using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VVO.UI;

public static class IconColors
{
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
                       "SystemControlForegroundBaseMediumBrush", application.ActualThemeVariant, out var resource)
                   && resource is ISolidColorBrush brush
                ? brush.Color
                : Colors.Gray;
        }
    }

    public static IBrush Brush(string? color)
    {
        return new SolidColorBrush(
            !string.IsNullOrWhiteSpace(color) && Color.TryParse(color, out var parsed) ? parsed : Default);
    }
}
