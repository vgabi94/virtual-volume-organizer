using System.CommandLine;
using VVO.Cli.Contract;

namespace VVO.Cli.Commands;

public static class FolderCommands
{
    public static Command Create(IServiceProvider services)
    {
        return new Command("folder", "Folders: scanned trees as they are listed in a virtual volume.")
        {
            List(services)
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
        });

        return command;
    }
}
