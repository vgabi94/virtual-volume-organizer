using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VVO.UI;

/// <summary>
/// The catalogue named on the command line. Associating .vvo with the application is what makes
/// a double-clicked catalogue arrive this way: the shell starts the executable with the file as
/// its only argument.
/// </summary>
public static class StartupCatalogue
{
    public const string Extension = ".vvo";

    /// <summary>
    /// The catalogue to open, or null when the application was started on nothing. Whether the
    /// file is there is left to the caller, which has somewhere to say so.
    /// </summary>
    public static string? From(IReadOnlyList<string>? args)
    {
        if (args == null)
            return null;

        // Switches are skipped rather than taken as paths: the framework reads its own from the
        // same list, and only a catalogue is being looked for here
        return args
            .Where(argument => !string.IsNullOrWhiteSpace(argument) && !argument.StartsWith('-'))
            .FirstOrDefault(argument => string.Equals(
                Path.GetExtension(argument), Extension, StringComparison.OrdinalIgnoreCase));
    }
}
