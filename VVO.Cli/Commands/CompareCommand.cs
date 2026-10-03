using System.CommandLine;
using VVO.Cli.Contract;
using VVO.Cli.Output;
using VVO.Core;
using VVO.Core.Models;
using VVO.Core.Services;

namespace VVO.Cli.Commands;

public static class CompareCommand
{
    public static Command Create(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var left = new Argument<Guid>("entry") { Description = "The folder entry to compare from." };
        var right = new Argument<Guid?>("other")
        {
            Description = "The folder entry to compare with. Leave out when comparing with --disk.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var disk = new Option<string>("--disk") { Description = "Compare with this folder as it is on disk now." }.TakesPath();
        var hidden = Scanning.CreateHiddenOption();
        var includeUnchanged = new Option<bool>("--include-unchanged") { Description = "List what is the same too." };
        var exitCode = new Option<bool>("--exit-code") { Description = "Exit with 1 when there are differences." };

        var command = new Command(
            "compare",
            "Show how a folder differs from another folder, or from a folder on disk. Added means present "
            + "on the right only. Writes nothing.")
        {
            left, right, db, disk, hidden, includeUnchanged, exitCode
        };

        command.SetJsonAction(services, async context =>
        {
            var otherId = context.ParseResult.GetValue(right);
            var diskPath = context.ParseResult.GetValue(disk);

            if ((otherId == null) == (diskPath == null))
                throw CliException.Usage("Compare with another folder entry or with --disk, one of the two.");

            await DatabaseFile.OpenExistingAsync(context, db);

            var (leftEntry, leftRecords) = await TreeOfAsync(context, context.ParseResult.GetValue(left));

            RootFolderMetadata rightEntry;
            IReadOnlyCollection<FileRecord> rightRecords;
            var skippedFolders = 0;
            object rightSide;

            if (otherId is { } id)
            {
                (rightEntry, rightRecords) = await TreeOfAsync(context, id);
                rightSide = Side(rightEntry, rightRecords);
            }
            else
            {
                // Anything the scan was refused reads as removed, so the count goes with the rows
                var scan = await Scanning.ScanAsync(context, diskPath!, hidden);
                rightEntry = scan.Metadata;
                rightRecords = scan.Records;
                skippedFolders = scan.SkippedFolders;
                rightSide = new { EntryId = (Guid?)null, Title = (string?)null, scan.Metadata.Path };
            }

            var results = await context.Service<IFolderCompareService>().CompareAsync(
                leftEntry, leftRecords, rightEntry, rightRecords,
                context.ParseResult.GetValue(includeUnchanged), context.Progress, context.CancellationToken);

            var rows = results
                .Select(result => ComparisonRowDto.From(result, leftStored: true, rightStored: otherId != null))
                .ToList();

            if (context.ParseResult.GetValue(exitCode) && results.Any(result => result.Status != ComparisonStatus.Unchanged))
            {
                context.ExitCode = ExitCode.Differences;
            }

            return new
            {
                Left = Side(leftEntry, leftRecords),
                Right = rightSide,
                SkippedFolders = skippedFolders,
                Counts = DifferenceCounts.From(results),
                Rows = rows
            };
        }, TableFormat.Comparison);

        return command;
    }

    // The whole tree, root record included: the comparison is anchored on it
    private static async Task<(RootFolderMetadata, IReadOnlyCollection<FileRecord>)> TreeOfAsync(
        CommandContext context, Guid entryId)
    {
        var database = context.Service<IDatabaseService>();

        var entry = (await database.FindItemsAsync<RootFolderMetadata>(item => item.Id == entryId)).SingleOrDefault()
            ?? throw CliException.NotFound($"There is no folder entry '{entryId}'.");

        var treeId = entry.TreeId;
        return (entry, await database.FindItemsAsync<FileRecord>(record => record.RootFolderId == treeId));
    }

    private static object Side(RootFolderMetadata entry, IReadOnlyCollection<FileRecord> records)
    {
        var root = records.Single(record => record.Id == entry.TreeId);
        return new { EntryId = (Guid?)entry.Id, Title = (string?)EntryPaths.TitleOf(entry, root), entry.Path };
    }
}
