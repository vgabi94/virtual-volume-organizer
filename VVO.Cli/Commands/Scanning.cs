using System.CommandLine;
using VVO.Cli.Output;
using VVO.Core.Services;

namespace VVO.Cli.Commands;

/// <summary>
/// Reading a folder from disk, for the commands that catalogue or compare against one.
/// </summary>
public static class Scanning
{
    /// <summary>
    /// Stands in for the GUI's setting, which the CLI does not read.
    /// </summary>
    public static Option<bool> CreateHiddenOption() => new("--hidden")
    {
        Description = "Include hidden and system files and folders, which a scan leaves out by default."
    };

    public static async Task<ScanResult> ScanAsync(CommandContext context, string path, Option<bool> hidden)
    {
        var full = Path.GetFullPath(path);

        if (File.Exists(full))
            throw CliException.Usage($"'{full}' is a file, not a folder.");

        if (!Directory.Exists(full))
            throw CliException.NotFound($"There is no folder at '{full}'.");

        return await context.Service<IFileScannerService>().ScanDirectoryAsync(
            full,
            context.Progress,
            context.CancellationToken,
            includeHiddenAndSystem: context.ParseResult.GetValue(hidden));
    }
}
