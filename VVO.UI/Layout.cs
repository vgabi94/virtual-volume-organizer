using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VVO.UI;

/// <summary>
/// Which of the two layouts the application is in. The modern one is a class on every window,
/// so every difference it makes is a style, and dialogs opened later arrive already wearing it.
/// </summary>
public static class Layout
{
    public const string ModernClass = "Modern";

    private static readonly HashSet<Window> Open = [];
    private static bool _tracking;

    public static bool IsModern { get; private set; }

    /// <summary>
    /// Takes the layout in place of the application, for the same reason as
    /// <see cref="Theme.Applying"/>: the windows of a test session outlive the test.
    /// </summary>
    public static Action<bool>? Applying { get; set; }

    /// <summary>Puts the hook back and the windows in the classic layout a session starts in.</summary>
    public static void Reset()
    {
        Applying = null;
        Apply(false);
    }

    /// <summary>Follows every window from now on, so one opened later is given the current layout.</summary>
    public static void Track()
    {
        if (_tracking)
            return;

        _tracking = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => Opened(window), RoutingStrategies.Direct);
        Window.WindowClosedEvent.AddClassHandler<Window>((window, _) => Open.Remove(window), RoutingStrategies.Direct);
    }

    public static void Apply(bool modern)
    {
        if (Applying != null)
        {
            Applying(modern);
            return;
        }

        IsModern = modern;

        foreach (var window in Open)
        {
            ApplyTo(window);
        }
    }

    /// <summary>Gives a window the current layout before it is first shown, so it is never drawn in the other.</summary>
    public static void ApplyTo(Window window)
    {
        window.Classes.Set(ModernClass, IsModern);
    }

    private static void Opened(Window window)
    {
        Open.Add(window);
        ApplyTo(window);
    }
}
