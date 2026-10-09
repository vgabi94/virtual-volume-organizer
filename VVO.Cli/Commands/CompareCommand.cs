using System.CommandLine;
using VVO.Cli.Contract;
using VVO.Cli.Mcp;
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
        var left = new Argument<Guid>("folder")
        {
            Description = "The folder to compare from: a folder entry id, for the top of its tree, or a folder's record id."
        };
        var right = new Argument<Guid?>("other")
        {
            Description = "The folder to compare with, named the same way. Leave out when comparing with --disk.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var disk = new Option<string>("--disk") { Description = "Compare with this folder as it is on disk now." }.TakesPath();
        var hidden = Scanning.CreateHiddenOption();
        var includeUnchanged = new Option<bool>("--include-unchanged") { Description = "List what is the same too." };
        var exitCode = new Option<bool>("--exit-code") { Description = "Exit with 1 when there are differences." }.CliOnly();

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
                throw CliException.Usage("Compare with another folder or with --disk, one of the two.");

            await DatabaseFile.OpenExistingAsync(context, db);

            var leftSide = await SideOfAsync(context, context.ParseResult.GetValue(left));

            Side rightSide;
            var skippedFolders = 0;

            if (otherId is { } id)
            {
                rightSide = await SideOfAsync(context, id);
            }
            else
            {
                // Anything the scan was refused reads as removed, so the count goes with the rows
                var scan = await Scanning.ScanAsync(context, diskPath!, hidden);
                skippedFolders = scan.SkippedFolders;
                rightSide = new Side(scan.Metadata, scan.Records, new ComparedSideDto(null, null, null, scan.Metadata.Path));
            }

            // Unchanged rows are always compared for, so they are counted whether listed or not
            var results = await context.Service<IFolderCompareService>().CompareAsync(
                leftSide.Anchor, leftSide.Records, rightSide.Anchor, rightSide.Records,
                includeUnchanged: true, context.Progress, context.CancellationToken);

            var rows = results
                .Where(result => context.ParseResult.GetValue(includeUnchanged) || result.Status != ComparisonStatus.Unchanged)
                .Select(result => ComparisonRowDto.From(result, leftStored: true, rightStored: otherId != null))
                .ToList();

            if (context.ParseResult.GetValue(exitCode) && results.Any(result => result.Status != ComparisonStatus.Unchanged))
            {
                context.ExitCode = ExitCode.Differences;
            }

            return new
            {
                Left = leftSide.Description,
                Right = rightSide.Description,
                SkippedFolders = skippedFolders,
                Counts = DifferenceCounts.From(results),
                Rows = rows
            };
        }, TableFormat.Comparison);

        return command;
    }

    // The comparison walks down from the folder the anchor names, so the whole tree is read
    // whichever folder in it that is
    private sealed record Side(RootFolderMetadata Anchor, IReadOnlyCollection<FileRecord> Records, ComparedSideDto Description);

    // An entry id stands for the top of its tree, as it does for ls
    private static async Task<Side> SideOfAsync(CommandContext context, Guid id)
    {
        var database = context.Service<IDatabaseService>();

        var entry = (await database.FindItemsAsync<RootFolderMetadata>(item => item.Id == id)).SingleOrDefault();
        if (entry != null)
        {
            var records = await TreeAsync(context, entry.TreeId);
            var root = Catalogue.RootOf(records.ToDictionary(record => record.Id), entry);

            return new Side(entry, records, new ComparedSideDto(entry.Id, root.Id, EntryPaths.TitleOf(entry, root), entry.Path));
        }

        var folder = (await database.FindItemsAsync<FileRecord>(record => record.Id == id)).SingleOrDefault()
            ?? throw CliException.NotFound($"There is no folder entry or catalogue record '{id}'.");

        if (!folder.IsFolder)
            throw CliException.Usage($"'{folder.Name}' is a file, not a folder.");

        // A folder whose parents never reach the top is damage, and below one caught in a loop of
        // parents the walk down would go round for ever
        await Catalogue.AncestorsAsync(context, folder);

        var tree = await TreeAsync(context, folder.RootFolderId);
        var listing = (await Catalogue.EntriesAsync(context)).Where(item => item.TreeId == folder.RootFolderId).ToList();

        // Each entry listing the tree can place the folder somewhere else on disk
        string? path = null;
        if (listing.Count == 1 && new EntryPaths(listing[0], string.Empty, tree).PhysicalPathOf(folder.Id) is { Length: > 0 } physical)
            path = physical;

        return new Side(
            new RootFolderMetadata { TreeId = folder.Id, Path = path ?? string.Empty },
            tree,
            new ComparedSideDto(null, folder.Id, folder.Name, path));
    }

    private static Task<IReadOnlyCollection<FileRecord>> TreeAsync(CommandContext context, Guid treeId) =>
        context.Service<IDatabaseService>().FindItemsAsync<FileRecord>(record => record.RootFolderId == treeId);
}
