using System;
using System.Threading.Tasks;
using Avalonia.Input.Platform;

namespace VVO.UI;

/// <summary>
/// Where copied text goes. A view model reaches the system clipboard through the main window,
/// which a headless test does not have; supplying <see cref="Writing"/> takes the text instead,
/// so a copy command can be covered without one.
/// </summary>
public static class TextClipboard
{
    public static Func<string, Task>? Writing { get; set; }

    public static void Reset() => Writing = null;

    /// <summary>
    /// Puts text on the clipboard, doing nothing with an empty string: a command that found
    /// nothing to copy should leave whatever is already there alone.
    /// </summary>
    public static async Task WriteAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (Writing != null)
        {
            await Writing(text);
            return;
        }

        var clipboard = Dialogs.Owner()?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
