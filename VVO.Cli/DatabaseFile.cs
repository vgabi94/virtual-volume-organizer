using System.CommandLine;
using VVO.Cli.Output;
using VVO.Core.Services;

namespace VVO.Cli;

/// <summary>
/// The catalogue a command works on, named by its --db option.
/// </summary>
public static class DatabaseFile
{
    public static Option<string> CreateOption() => new(Confirmation.DbOptionName)
    {
        Description = "The .vvo catalogue to work on.",
        Required = true
    };

    /// <summary>
    /// Opens a catalogue that has to be there already. LiteDB creates whatever file it is pointed
    /// at, so a mistyped path would otherwise leave an empty catalogue behind and report success.
    /// </summary>
    /// <returns>The full path of the catalogue.</returns>
    public static async Task<string> OpenExistingAsync(CommandContext context, Option<string> option)
    {
        var path = FullPath(context, option);

        if (!File.Exists(path))
            throw CliException.NotFound($"There is no catalogue at '{path}'.");

        await context.Service<IDatabaseService>().EnsureDatabaseReadyAsync(path);
        return path;
    }

    /// <summary>
    /// Creates a catalogue, replacing whatever file is at the path once the user has agreed to it.
    /// </summary>
    /// <returns>The full path of the catalogue.</returns>
    public static async Task<string> CreateOrReplaceAsync(CommandContext context, string path)
    {
        path = FullPath(path);
        var replace = ConfirmReplacing(context, path);

        await context.Service<IDatabaseService>().EnsureDatabaseReadyAsync(path, replace);
        return path;
    }

    /// <summary>
    /// Asks before a command writes a file over one that is already there, as the GUI's save
    /// picker does.
    /// </summary>
    /// <returns>Whether there was a file to replace.</returns>
    public static bool ConfirmReplacing(CommandContext context, string path)
    {
        if (!File.Exists(path))
            return false;

        Confirmation.Require(context, new ConfirmationRequest(
            "Replace File",
            path,
            $"'{path}' already exists and will be replaced. This cannot be undone.",
            "replace"));

        return true;
    }

    public static string FullPath(CommandContext context, Option<string> option) =>
        FullPath(context.ParseResult.GetRequiredValue(option));

    private static string FullPath(string path)
    {
        var full = Path.GetFullPath(path);

        if (Directory.Exists(full))
            throw CliException.Usage($"'{full}' is a folder, not a catalogue file.");

        return full;
    }
}
