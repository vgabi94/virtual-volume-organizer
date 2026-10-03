using System.CommandLine;
using VVO.Cli.Contract;
using VVO.Cli.Output;
using VVO.Core.Models;
using VVO.Core.Services;

namespace VVO.Cli.Commands;

/// <summary>
/// Looking inside the catalogued trees. Each takes a folder entry id, which stands for the top
/// of its tree, or the id of any catalogue record.
/// </summary>
public static class BrowseCommands
{
    private const string IdDescription = "A folder entry id, for the top of its tree, or a catalogue record id.";

    public static IEnumerable<Command> Create(IServiceProvider services)
    {
        yield return List(services);
        yield return Tree(services);
        yield return Stat(services);
    }

    private static Command List(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var id = new Argument<Guid>("id") { Description = IdDescription };

        var command = new Command("ls", "List what a folder holds, folders first.") { id, db };

        command.SetJsonAction(services, async context =>
        {
            await DatabaseFile.OpenExistingAsync(context, db);

            var (folder, entries) = await Catalogue.LocateAsync(context, context.ParseResult.GetValue(id));
            RequireFolder(folder);

            var children = await context.Service<IDatabaseService>()
                .FindItemsAsync<FileRecord>(record => record.ParentId == folder.Id);

            var known = (await Catalogue.AncestorsAsync(context, folder)).Append(folder).Concat(children).ToList();
            var placer = await Catalogue.PlacerAsync(context, entries, known);

            return new
            {
                Folder = placer.Describe(folder),
                Children = Catalogue.InExplorerOrder(children).Select(placer.Describe).ToList()
            };
        }, TableFormat.Listing);

        return command;
    }

    private static Command Tree(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var id = new Argument<Guid>("id") { Description = IdDescription };
        var depth = new Option<int?>("--depth")
        {
            Description = "How many levels below the folder to go. Everything below it when left out."
        };

        var command = new Command(
            "tree",
            "List everything below a folder, in the order a tree view shows it: each folder followed by what it "
            + "holds. Each entry carries its depth and its parentId, so the list stays flat however deep the folder.")
        {
            id, db, depth
        };

        command.SetJsonAction(services, async context =>
        {
            var levels = context.ParseResult.GetValue(depth);
            if (levels < 1)
                throw CliException.Usage("--depth has to be at least 1.");

            await DatabaseFile.OpenExistingAsync(context, db);

            var (folder, entries) = await Catalogue.LocateAsync(context, context.ParseResult.GetValue(id));
            RequireFolder(folder);

            // The whole tree at once: walking it a folder at a time would be a query per folder
            var records = await context.Service<IDatabaseService>()
                .FindItemsAsync<FileRecord>(record => record.RootFolderId == folder.RootFolderId);

            var children = records
                .Where(record => record.ParentId != null)
                .ToLookup(record => record.ParentId!.Value);

            var placer = await Catalogue.PlacerAsync(context, entries, records);

            // Walked with a stack of its own rather than by recursion, which a deep enough folder
            // would run out of
            var below = new List<TreeEntryDto>();
            var pending = new Stack<(FileRecord Record, int Depth)>();
            PushChildren(folder, 0);

            while (pending.TryPop(out var next))
            {
                below.Add(new TreeEntryDto(next.Depth, placer.Describe(next.Record)));

                if (next.Record.IsFolder && (levels == null || next.Depth < levels))
                {
                    PushChildren(next.Record, next.Depth);
                }
            }

            return new { Folder = placer.Describe(folder), Below = below };

            void PushChildren(FileRecord parent, int depthOfParent)
            {
                foreach (var child in Catalogue.InExplorerOrder(children[parent.Id]).Reverse())
                {
                    pending.Push((child, depthOfParent + 1));
                }
            }
        }, TableFormat.Tree);

        return command;
    }

    private static Command Stat(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var id = new Argument<Guid>("id") { Description = IdDescription };

        var command = new Command("stat", "Describe one file or folder and every place it is listed.") { id, db };

        command.SetJsonAction(services, async context =>
        {
            await DatabaseFile.OpenExistingAsync(context, db);

            var (record, entries) = await Catalogue.LocateAsync(context, context.ParseResult.GetValue(id));

            var known = (await Catalogue.AncestorsAsync(context, record)).Append(record).ToList();
            var placer = await Catalogue.PlacerAsync(context, entries, known);

            int? childCount = record.IsFolder
                ? await context.Service<IDatabaseService>().CountItemsAsync<FileRecord>(child => child.ParentId == record.Id)
                : null;

            return new { Record = placer.Describe(record), ChildCount = childCount };
        });

        return command;
    }

    private static void RequireFolder(FileRecord record)
    {
        if (!record.IsFolder)
            throw CliException.Usage($"'{record.Name}' is a file, not a folder.");
    }
}
