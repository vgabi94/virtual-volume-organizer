using System.CommandLine;
using VVO.Cli.Output;
using VVO.Core.Services;

namespace VVO.Cli;

/// <summary>
/// The catalogue a command works on, named by its --db option.
/// </summary>
public static class DatabaseFile
{
    public static Option<string> CreateOption() => new Option<string>("--db")
    {
        Description = "The .vvo catalogue to work on.",
        Required = true
    }.TakesPath();

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

        // Core fills an empty file in as a new catalogue, which is right for a save dialog and wrong
        // for a command that was asked to read one
        if (new FileInfo(path).Length == 0)
            throw new CliException(ExitCode.Error, ErrorCodes.InvalidDatabase, $"'{path}' is empty: it holds no catalogue.");

        await context.Service<IDatabaseService>().EnsureDatabaseReadyAsync(path);
        return path;
    }

    /// <summary>
    /// Opens a catalogue a command is about to change. One that can only be read is refused here,
    /// before the command scans anything or asks the user anything.
    /// </summary>
    /// <returns>The full path of the catalogue.</returns>
    public static async Task<string> OpenForWritingAsync(CommandContext context, Option<string> option)
    {
        var path = await OpenExistingAsync(context, option);

        if (context.Service<IDatabaseService>().IsReadOnly)
            throw new CatalogueReadOnlyException(path);

        return path;
    }

    /// <summary>
    /// Creates a catalogue, replacing whatever file is at the path once the user has agreed to it.
    /// </summary>
    /// <returns>The full path of the catalogue.</returns>
    public static async Task<string> CreateOrReplaceAsync(CommandContext context, string path)
    {
        path = FullPath(path);
        var replace = await ConfirmReplacingAsync(context, path);

        await context.Service<IDatabaseService>().EnsureDatabaseReadyAsync(path, replace);
        return path;
    }

    /// <summary>
    /// Asks before a command writes a file over one that is already there, as the GUI's save
    /// picker does.
    /// </summary>
    /// <returns>Whether there was a file to replace.</returns>
    public static async Task<bool> ConfirmReplacingAsync(CommandContext context, string path)
    {
        if (!File.Exists(path))
            return false;

        await Confirmation.RequireAsync(context, new ConfirmationRequest(
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
