using System;
using System.Diagnostics;
using System.Threading.Tasks;
using MsBox.Avalonia.Enums;

namespace VVO.UI;

public static class Logger
{
    /// <summary>
    /// Puts a failure to the user. Every one of these is reported from a handler that hands
    /// back before its work is done, so nothing is waiting to catch what it throws.
    /// </summary>
    public static async Task ShowErrorAsync(Exception e)
    {
        try
        {
            await Dialogs.TellAsync("Error", $"Error: {e.Message}", Icon.Error);
        }
        catch (Exception failedToReport)
        {
            // The window the message would have gone on is itself gone, which is what a
            // failure during shutdown looks like. There is nowhere left to say so, and
            // letting this out would take the application down over a message about a
            // problem the user can no longer be told about.
            Trace.WriteLine($"Could not report '{e.Message}': {failedToReport.Message}");
        }
    }
}
