using System.CommandLine;
using VVO.Cli.Contract;
using VVO.Cli.Output;
using VVO.Core;
using VVO.Core.Models;
using VVO.Core.Services;

namespace VVO.Cli.Commands;

public static class FolderCommands
{
    public static Command Create(IServiceProvider services)
    {
        return new Command("folder", "Folders: scanned trees as they are listed in a virtual volume.")
        {
            List(services),
            Scan(services),
            Update(services),
            Copy(services),
            Move(services),
            Delete(services),
            Rescan(services)
        };
    }

    private static Command List(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var volume = new Option<Guid?>("--volume") { Description = "Only the folders in this virtual volume." };

        var command = new Command("list", "List the folders, in every virtual volume or in one.") { db, volume };

        command.SetJsonAction(services, async context =>
        {
            await DatabaseFile.OpenExistingAsync(context, db);

            var entries = await Catalogue.EntriesAsync(context);

            if (context.ParseResult.GetValue(volume) is { } volumeId)
            {
                await Catalogue.VolumeAsync(context, volumeId);
                entries = entries.Where(entry => entry.VirtualVolumeId == volumeId).ToList();
            }

            var roots = await Catalogue.RootsAsync(context);
            var recordCounts = new Dictionary<Guid, int>();

            var folders = new List<FolderEntryDto>();
            foreach (var entry in entries)
            {
                if (!recordCounts.TryGetValue(entry.TreeId, out var count))
                {
                    count = await Catalogue.RecordCountAsync(context, entry.TreeId);
                    recordCounts[entry.TreeId] = count;
                }

                folders.Add(FolderEntryDto.From(entry, Catalogue.RootOf(roots, entry), count));
            }

            return folders
                .OrderBy(folder => folder.VolumeId)
                .ThenBy(folder => folder.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(folder => folder.Id)
                .ToList();
        }, TableFormat.Folders);

        return command;
    }

    private static Command Scan(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var path = new Argument<string>("path") { Description = "The folder on disk to catalogue." }.TakesPath();
        var volume = new Option<Guid>("--volume") { Description = "The virtual volume to list it in.", Required = true };
        var appearance = new AppearanceOptions();
        var hidden = Scanning.CreateHiddenOption();

        var command = new Command("scan", "Catalogue a folder from disk into a virtual volume.")
        {
            path, db, volume, hidden
        };
        appearance.AddTo(command);

        command.SetJsonAction(services, async context =>
        {
            var chosen = appearance.Read(context);

            await DatabaseFile.OpenForWritingAsync(context, db);

            // Before the scan, which can take a long time to find out about a mistyped id
            var volumeId = (await Catalogue.VolumeAsync(context, context.ParseResult.GetValue(volume))).Id;

            var scan = await Scanning.ScanAsync(context, context.ParseResult.GetRequiredValue(path), hidden);
            var metadata = scan.Metadata with
            {
                Label = Blank(chosen.Label),
                Description = Blank(chosen.Description),
                Icon = chosen.Icon,
                Color = chosen.Color
            };

            context.Progress.Report("Folder scan complete. Saving to database...");
            var entry = await context.Service<IVirtualVolumeService>().AddFolderAsync(
                volumeId, metadata, scan.Records, context.Progress, context.CancellationToken);

            return new { Folder = await Catalogue.DescribeAsync(context, entry), scan.SkippedFolders };
        });

        return command;
    }

    private static Command Update(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var id = new Argument<Guid>("id") { Description = "The folder entry." };
        var appearance = new AppearanceOptions();

        var command = new Command(
            "update",
            "Change how a folder is listed. What is not given is left as it is; an empty --label or "
            + "--description clears it, and a folder without a label is listed under its scanned name.")
        {
            id, db
        };
        appearance.AddTo(command);

        command.SetJsonAction(services, async context =>
        {
            var chosen = appearance.Read(context);
            if (!chosen.Any)
                throw CliException.Usage("Give at least one of --label, --description, --icon or --color.");

            await DatabaseFile.OpenForWritingAsync(context, db);

            var entry = await Catalogue.EntryAsync(context, context.ParseResult.GetValue(id));
            var updated = await context.Service<IVirtualVolumeService>().UpdateFolderAsync(
                entry.Id,
                chosen.LabelGiven ? chosen.Label : entry.Label,
                chosen.DescriptionGiven ? chosen.Description : entry.Description,
                chosen.IconGiven ? chosen.Icon : entry.Icon,
                chosen.ColorGiven ? chosen.Color : entry.Color);

            return await Catalogue.DescribeAsync(context, updated);
        });

        return command;
    }

    private static Command Copy(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var id = new Argument<Guid>("id") { Description = "The folder entry to copy." };
        var to = new Option<Guid>("--to") { Description = "The virtual volume to put the copy in.", Required = true };
        var label = new Option<string>("--label") { Description = "A label for the copy. It keeps the original's otherwise." };

        var command = new Command(
            "copy",
            "List a folder a second time, sharing its scanned tree. Copying into its own volume duplicates it.")
        {
            id, db, to, label
        };

        command.SetJsonAction(services, async context =>
        {
            await DatabaseFile.OpenForWritingAsync(context, db);

            var copy = await context.Service<IVirtualVolumeService>().CopyFolderAsync(
                context.ParseResult.GetValue(id),
                context.ParseResult.GetValue(to),
                context.ParseResult.GetValue(label)?.Trim());

            return await Catalogue.DescribeAsync(context, copy);
        });

        return command;
    }

    private static Command Move(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var id = new Argument<Guid>("id") { Description = "The folder entry to move." };
        var to = new Option<Guid>("--to") { Description = "The virtual volume to move it to.", Required = true };

        var command = new Command("move", "Move a folder to another virtual volume.") { id, db, to };

        command.SetJsonAction(services, async context =>
        {
            await DatabaseFile.OpenForWritingAsync(context, db);

            var entryId = context.ParseResult.GetValue(id);
            await context.Service<IVirtualVolumeService>().MoveFolderAsync(entryId, context.ParseResult.GetValue(to));

            return await Catalogue.DescribeAsync(context, await Catalogue.EntryAsync(context, entryId));
        });

        return command;
    }

    private static Command Delete(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var id = new Argument<Guid>("id") { Description = "The folder entry to delete." };

        var command = new Command(
            "delete",
            "Delete a folder from its virtual volume, with its scanned tree unless a copy still uses it. "
            + "Asks the user to type 'delete' first.")
        {
            id, db
        };

        command.SetJsonAction(services, async context =>
        {
            await DatabaseFile.OpenForWritingAsync(context, db);

            var entry = await Catalogue.EntryAsync(context, context.ParseResult.GetValue(id));
            var folder = await Catalogue.DescribeAsync(context, entry);

            Confirmation.RequireDeletion(context, DeletionWarnings.ForFolder(folder.Title));

            await context.Service<IVirtualVolumeService>()
                .RemoveFolderAsync(entry.Id, context.Progress, context.CancellationToken);

            return new { Deleted = folder };
        });

        return command;
    }

    private const int ShownDifferences = 50;

    private static Command Rescan(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var id = new Argument<Guid>("id") { Description = "The folder entry to rescan." };
        var path = new Argument<string?>("path")
        {
            Description = "Where to read it from. Where it was last scanned from when left out.",
            Arity = ArgumentArity.ZeroOrOne
        }.TakesPath();
        var hidden = Scanning.CreateHiddenOption();

        var command = new Command(
            "rescan",
            "Read a folder from disk again and replace what is catalogued for it. Shows what would change "
            + "and asks the user to type 'update' first. 'compare <id> --disk <path>' shows the same "
            + "differences without asking.")
        {
            id, path, db, hidden
        };

        command.SetJsonAction(services, async context =>
        {
            var dbPath = await DatabaseFile.OpenForWritingAsync(context, db);

            var entry = await Catalogue.EntryAsync(context, context.ParseResult.GetValue(id));
            var source = context.ParseResult.GetValue(path) ?? entry.Path;
            if (string.IsNullOrWhiteSpace(source))
                throw CliException.Usage("The folder has no path on disk recorded. Give the one to read it from.");

            var treeId = entry.TreeId;
            var catalogued = await context.Service<IDatabaseService>()
                .FindItemsAsync<FileRecord>(record => record.RootFolderId == treeId);
            var title = EntryPaths.TitleOf(entry, catalogued.Single(record => record.Id == treeId));

            var scan = await Scanning.ScanAsync(context, source, hidden);
            var differences = await context.Service<IFolderCompareService>().CompareAsync(
                entry, catalogued, scan.Metadata, scan.Records,
                includeUnchanged: false, context.Progress, context.CancellationToken);

            var counts = DifferenceCounts.From(differences);

            Confirmation.Require(context, new ConfirmationRequest(
                "Rescan Folder",
                title,
                $"{ScanWarnings.RescanProposal} This cannot be undone.",
                "update",
                Explanation(scan, differences, counts),
                new Dictionary<string, object?>
                {
                    ["path"] = scan.Metadata.Path,
                    ["counts"] = counts,
                    ["skippedFolders"] = scan.SkippedFolders,
                    ["preview"] = Confirmation.CommandLine(
                        "compare", entry.Id.ToString(), "--disk", scan.Metadata.Path, "--db", dbPath)
                }));

            var updated = await context.Service<IVirtualVolumeService>().UpdateFolderContentsAsync(
                entry.Id, scan.Metadata, scan.Records, context.Progress, context.CancellationToken);

            return new
            {
                Folder = await Catalogue.DescribeAsync(context, updated),
                Counts = counts,
                scan.SkippedFolders
            };
        });

        return command;
    }

    // What the GUI shows before an update: the warning about what was not read, then the changes
    private static List<string> Explanation(
        ScanResult scan, IReadOnlyList<ComparisonResult> differences, DifferenceCounts counts)
    {
        var lines = new List<string>();

        if (scan.SkippedFolders > 0)
        {
            lines.Add(ScanWarnings.SkippedTitle);
            lines.Add(ScanWarnings.DescribeSkipped(scan.SkippedFolders, scan.Metadata.Path));
            lines.Add(string.Empty);
        }

        var shown = differences.Where(row => !DifferenceCounts.IsOnlyAParent(row)).ToList();

        lines.Add(shown.Count == 0
            ? "Nothing has changed since the last scan."
            : $"{counts.Added} added, {counts.Removed} removed, {counts.Changed} changed:");

        foreach (var row in shown.Take(ShownDifferences))
        {
            lines.Add($"  {Describe(row)}");
        }

        if (shown.Count > ShownDifferences)
        {
            lines.Add($"  and {shown.Count - ShownDifferences} more");
        }

        lines.Add(string.Empty);
        return lines;
    }

    private static string Describe(ComparisonResult row)
    {
        var mark = row.Status switch
        {
            ComparisonStatus.Added => "+",
            ComparisonStatus.Removed => "-",
            _ => "~"
        };

        var isFolder = (row.Left ?? row.Right)?.IsFolder == true;
        var name = isFolder ? $"{row.RelativePath}{CataloguePath.Separator}" : row.RelativePath;

        var details = new List<string>();
        if (row.Changes.HasFlag(ChangeKind.Size)) details.Add("size");
        if (row.Changes.HasFlag(ChangeKind.Modified)) details.Add("modified");
        if (row.DescendantCount is > 0) details.Add($"{row.DescendantCount} inside");

        return details.Count == 0 ? $"{mark} {name}" : $"{mark} {name} ({string.Join(", ", details)})";
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// How a folder is listed. Each option tells apart being left out, which keeps what is there,
    /// from being given empty, which clears it.
    /// </summary>
    private sealed class AppearanceOptions
    {
        private readonly Option<string> _label = new("--label") { Description = "The name to list the folder under." };
        private readonly Option<string> _description = new("--description") { Description = "A note shown with the folder." };
        private readonly Option<string> _icon = new("--icon")
        {
            Description = $"One of: {string.Join(", ", IconKeys.Folders)}."
        };
        private readonly Option<string> _color = new("--color")
        {
            Description = $"#RRGGBB or #AARRGGBB, or '{Appearance.NoColor}' for the application's own colour."
        };

        public void AddTo(Command command)
        {
            command.Options.Add(_label);
            command.Options.Add(_description);
            command.Options.Add(_icon);
            command.Options.Add(_color);
        }

        public Chosen Read(CommandContext context)
        {
            var label = context.ParseResult.GetResult(_label) != null;
            var description = context.ParseResult.GetResult(_description) != null;
            var icon = context.ParseResult.GetValue(_icon);
            var color = context.ParseResult.GetValue(_color);

            return new Chosen(
                label, context.ParseResult.GetValue(_label),
                description, context.ParseResult.GetValue(_description),
                icon != null, icon != null ? Appearance.FolderIcon(icon) : null,
                color != null, color != null ? Appearance.Color(color) : null);
        }
    }

    private sealed record Chosen(
        bool LabelGiven, string? Label,
        bool DescriptionGiven, string? Description,
        bool IconGiven, string? Icon,
        bool ColorGiven, string? Color)
    {
        public bool Any => LabelGiven || DescriptionGiven || IconGiven || ColorGiven;
    }
}
