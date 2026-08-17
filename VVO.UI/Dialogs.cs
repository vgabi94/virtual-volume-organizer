using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace VVO.UI;

/// <summary>
/// Where a dialog is shown and how its answer comes back.
///
/// The application shows a dialog over its main window and waits for the user. A test has no
/// main window to show one over — a headless application has no desktop lifetime at all — and
/// nobody to answer it, so it supplies both here. Without that, every command that asks the
/// user something stops at the first line and cannot be covered.
/// </summary>
public static class Dialogs
{
    private static readonly Func<Window?> MainWindow = () =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

    /// <summary>
    /// The window a dialog is shown over. Null leaves the command with nothing to open onto,
    /// which is how it behaves before the main window is up.
    /// </summary>
    public static Func<Window?> Owner { get; set; } = MainWindow;

    /// <summary>
    /// Answers a dialog in place of the user, taking the dialog itself so its view model can be
    /// filled in first. Null shows it for real and waits.
    /// </summary>
    public static Func<Window, Task<bool>>? Answer { get; set; }

    /// <summary>
    /// Takes a message meant for the user. Null puts a real message box on screen.
    /// </summary>
    public static Func<string, string, Task>? Told { get; set; }

    public static Task<bool> ShowAsync(Window dialog, Window owner)
    {
        return Answer?.Invoke(dialog) ?? dialog.ShowDialog<bool>(owner);
    }

    public static async Task TellAsync(string title, string message, Icon icon = Icon.Info)
    {
        if (Told != null)
        {
            await Told(title, message);
            return;
        }

        await MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok, icon).ShowAsync();
    }

    /// <summary>
    /// Puts everything back the way the running application wants it.
    /// </summary>
    public static void Reset()
    {
        Owner = MainWindow;
        Answer = null;
        Told = null;
    }
}
