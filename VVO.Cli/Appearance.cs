using System.Text.RegularExpressions;
using VVO.Cli.Output;
using VVO.Core;

namespace VVO.Cli;

/// <summary>
/// The icons and colours a command accepts: the ones the GUI offers, so nothing the CLI writes
/// shows up blank there.
/// </summary>
public static partial class Appearance
{
    /// <summary>
    /// Given for --color, puts the icon back on the application's own colour.
    /// </summary>
    public const string NoColor = "none";

    public static string VolumeIcon(string key) => Icon(key, IconKeys.Volumes, "virtual volume");

    public static string FolderIcon(string key) => Icon(key, IconKeys.Folders, "folder");

    /// <returns>Null for <see cref="NoColor"/>.</returns>
    public static string? Color(string value)
    {
        if (string.Equals(value, NoColor, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!HexColor().IsMatch(value))
        {
            throw CliException.Usage(
                $"'{value}' is not a colour. Give it as #RRGGBB or #AARRGGBB, or '{NoColor}' for the default.");
        }

        return value;
    }

    // Matched without regard to case, then stored as the GUI spells it
    private static string Icon(string key, IReadOnlyList<string> keys, string what)
    {
        return keys.FirstOrDefault(known => string.Equals(known, key, StringComparison.OrdinalIgnoreCase))
            ?? throw CliException.Usage(
                $"'{key}' is not a {what} icon. Choose one of: {string.Join(", ", keys)}.");
    }

    [GeneratedRegex("^#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex HexColor();
}
