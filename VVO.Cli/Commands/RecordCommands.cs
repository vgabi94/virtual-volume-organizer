using System.CommandLine;
using VVO.Cli.Output;
using VVO.Core;
using VVO.Core.Models;
using VVO.Core.Services;

namespace VVO.Cli.Commands;

/// <summary>
/// Adding to and taking from a catalogued tree, the way the explorer does. Neither touches the
/// files on disk.
/// </summary>
public static class RecordCommands
{
    public static IEnumerable<Command> Create(IServiceProvider services)
    {
        yield return Add(services);
        yield return Remove(services);
    }

    private static Command Add(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var parent = new Argument<Guid>("parent")
        {
            Description = "The catalogued folder to add to: a folder record id, or a folder entry id for the top of its tree."
        };
        var paths = new Argument<string[]>("paths")
        {
            Description = "Files and folders on disk to add. Folders are scanned with everything under them.",
            Arity = ArgumentArity.OneOrMore
        }.TakesPath();
        var hidden = Scanning.CreateHiddenOption();

        var command = new Command(
            "add",
            "Add files and folders from disk under a catalogued folder. A name already there is skipped, with "
            + "everything under it.")
        {
            parent, paths, db, hidden
        };

        command.SetJsonAction(services, async context =>
        {
            await DatabaseFile.OpenForWritingAsync(context, db);

            var (folder, _) = await Catalogue.LocateAsync(context, context.ParseResult.GetValue(parent));
            if (!folder.IsFolder)
                throw CliException.Usage($"'{folder.Name}' is a file. Files can only be added under a folder.");

            // All of them checked before any is scanned, which for a folder can take a while
            var sources = context.ParseResult.GetRequiredValue(paths).Select(Path.GetFullPath).ToList();
            foreach (var source in sources.Where(source => !File.Exists(source) && !Directory.Exists(source)))
            {
                throw CliException.NotFound($"There is no file or folder at '{source}'.");
            }

            var records = new List<FileRecord>();
            var skippedFolders = 0;

            foreach (var source in sources)
            {
                if (Directory.Exists(source))
                {
                    var scan = await Scanning.ScanAsync(context, source, hidden);
                    skippedFolders += scan.SkippedFolders;
                    records.AddRange(Graft(scan.Records, folder));
                }
                else
                {
                    var file = await context.Service<IFileScannerService>().ReadFileAsync(source);
                    records.Add(file with { ParentId = folder.Id, RootFolderId = folder.RootFolderId });
                }
            }

            var result = await context.Service<IVirtualVolumeService>()
                .AddRecordsAsync(folder.Id, records, context.Progress, context.CancellationToken);

            // Two of the same name are one added and one skipped, so each skip accounts for one
            var unaccounted = result.Skipped.ToList();
            var added = records
                .Where(record => record.ParentId == folder.Id && !unaccounted.Remove(record.Name))
                .Select(record => record.Name)
                .ToList();

            return new
            {
                ParentId = folder.Id,
                Added = added,
                result.Skipped,
                SkippedFolders = skippedFolders,
                Tree = new { Id = result.Root.Id, result.Root.Size }
            };
        });

        return command;
    }

    private static Command Remove(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        // Read as text: given a list, the parser turns a value that is not a GUID into an empty one
        var ids = new Argument<string[]>("ids")
        {
            Description = "Catalogue record ids of the files and folders to remove.",
            Arity = ArgumentArity.OneOrMore
        };

        var command = new Command(
            "rm",
            "Remove files and folders from the catalogue, folders with everything under them. The files on "
            + "disk are left alone. Asks the user to type 'delete' first.")
        {
            ids, db
        };

        command.SetJsonAction(services, async context =>
        {
            await DatabaseFile.OpenForWritingAsync(context, db);

            var wanted = context.ParseResult.GetRequiredValue(ids)
                .Select(id => Guid.TryParse(id, out var parsed)
                    ? parsed
                    : throw CliException.Usage($"'{id}' is not a catalogue record id."))
                .Distinct()
                .ToList();
            var found = await context.Service<IDatabaseService>()
                .FindItemsAsync<FileRecord>(record => wanted.Contains(record.Id));

            // Every id is checked before anything is asked, so a mistake costs nothing
            foreach (var id in wanted.Where(id => found.All(record => record.Id != id)))
            {
                throw CliException.NotFound($"There is no catalogue record '{id}'.");
            }

            foreach (var root in found.Where(record => record.ParentId == null))
            {
                throw CliException.Usage(
                    $"'{root.Name}' is the top of its tree. Delete the folder entry with 'folder delete' instead.");
            }

            var records = found.ToList();
            Confirmation.RequireDeletion(context, records.Count == 1
                ? records[0].IsFolder
                    ? DeletionWarnings.ForCatalogueFolder(records[0].Name)
                    : DeletionWarnings.ForCatalogueFile(records[0].Name)
                : DeletionWarnings.ForCatalogueItems(records.Count, records.Any(record => record.IsFolder)));

            var roots = await context.Service<IVirtualVolumeService>()
                .RemoveRecordsAsync(wanted, context.Progress, context.CancellationToken);

            return new
            {
                Removed = wanted,
                Trees = roots.Select(root => new { root.Id, root.Size }).ToList()
            };
        });

        return command;
    }

    // As the explorer grafts a scan: its root becomes a folder under the parent, in the parent's tree
    private static IEnumerable<FileRecord> Graft(IEnumerable<FileRecord> scanned, FileRecord parent) =>
        scanned.Select(record => record with
        {
            RootFolderId = parent.RootFolderId,
            ParentId = record.ParentId ?? parent.Id
        });
}
