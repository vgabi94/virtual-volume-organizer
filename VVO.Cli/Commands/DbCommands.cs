using System.CommandLine;
using VVO.Core.Models;
using VVO.Core.Services;

namespace VVO.Cli.Commands;

public static class DbCommands
{
    public static Command Create(IServiceProvider services)
    {
        return new Command("db", "Create catalogues and look after them.")
        {
            New(services),
            Info(services)
        };
    }

    private static Command New(IServiceProvider services)
    {
        var path = new Argument<string>("path") { Description = "Where to create the .vvo file." }.TakesPath();
        var name = new Option<string>("--name")
        {
            Description = "The catalogue's name. Defaults to the file name without its extension."
        };

        var command = new Command("new", "Create an empty catalogue. Asks before replacing a file already there.")
        {
            path,
            name
        };

        command.SetJsonAction(services, async context =>
        {
            var created = await DatabaseFile.CreateOrReplaceAsync(context, context.ParseResult.GetRequiredValue(path));

            var catalogueName = context.ParseResult.GetValue(name);
            if (string.IsNullOrWhiteSpace(catalogueName))
            {
                catalogueName = Path.GetFileNameWithoutExtension(created);
            }

            // As the GUI's New Database records it
            await context.Service<IDatabaseService>().InsertItemsAsync(
                [new DatabaseMetadata { Name = catalogueName, Path = created }]);

            return new { Path = created, Name = catalogueName };
        });

        return command;
    }

    private static Command Info(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var command = new Command("info", "Describe a catalogue: its name, size and what it holds.") { db };

        command.SetJsonAction(services, async context =>
        {
            var path = await DatabaseFile.OpenExistingAsync(context, db);
            var database = context.Service<IDatabaseService>();

            var metadata = await database.ReadItemsAsync<DatabaseMetadata>();
            var file = new FileInfo(path);

            return new
            {
                Path = path,
                Name = metadata.FirstOrDefault()?.Name,
                FileSize = file.Length,
                LastWrite = file.LastWriteTimeUtc,
                VolumeCount = await database.CountItemsAsync<VirtualVolumeRecord>(),
                FolderCount = await database.CountItemsAsync<RootFolderMetadata>(),
                TreeCount = await database.CountItemsAsync<FileRecord>(record => record.ParentId == null),
                RecordCount = await database.CountItemsAsync<FileRecord>(record => record.ParentId != null)
            };
        });

        return command;
    }
}
