using System.CommandLine;
using System.Text.Json;
using VVO.Cli.Output;
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
            Info(services),
            Copy(services),
            Shrink(services),
            Export(services),
            Import(services)
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

    private static Command Copy(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var target = new Argument<string>("target") { Description = "Where to write the copy." }.TakesPath();

        var command = new Command("copy", "Save a copy of a catalogue. Asks before replacing a file already there.")
        {
            target, db
        };

        command.SetJsonAction(services, async context =>
        {
            var source = await DatabaseFile.OpenExistingAsync(context, db);

            var path = Path.GetFullPath(context.ParseResult.GetRequiredValue(target));
            if (string.Equals(path, source, StringComparison.OrdinalIgnoreCase))
                throw CliException.Usage("A catalogue cannot be copied onto itself.");

            if (Directory.Exists(path))
                throw CliException.Usage($"'{path}' is a folder. Give the file to write the copy to.");

            DatabaseFile.ConfirmReplacing(context, path);
            await context.Service<IDatabaseService>().CopyToAsync(path);

            return new { Path = path };
        });

        return command;
    }

    private static Command Shrink(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var command = new Command(
            "shrink",
            "Reclaim the space deleted records left behind. The catalogue as it stood before is kept beside it.")
        {
            db
        };

        command.SetJsonAction(services, async context =>
        {
            var path = await DatabaseFile.OpenForWritingAsync(context, db);
            var database = context.Service<IDatabaseService>();

            var before = new FileInfo(path).Length;
            await database.ShrinkDatabaseAsync(context.Progress, context.CancellationToken);
            var after = new FileInfo(path).Length;

            return new { Path = path, SizeBefore = before, SizeAfter = after, BackupPath = database.ShrinkBackupPath() };
        });

        return command;
    }

    private static Command Export(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var json = new Argument<string>("json") { Description = "The JSON file to write." }.TakesPath();

        var command = new Command(
            "export",
            "Write the whole catalogue to one JSON file. Asks before replacing a file already there.")
        {
            json, db
        };

        command.SetJsonAction(services, async context =>
        {
            var catalogue = await DatabaseFile.OpenExistingAsync(context, db);

            var path = Path.GetFullPath(context.ParseResult.GetRequiredValue(json));
            if (string.Equals(path, catalogue, StringComparison.OrdinalIgnoreCase))
                throw CliException.Usage("A catalogue cannot be exported over itself.");

            if (Directory.Exists(path))
                throw CliException.Usage($"'{path}' is a folder. Give the file to write the export to.");

            DatabaseFile.ConfirmReplacing(context, path);
            await context.Service<IDatabaseTransferService>().ExportAsync(path, context.Progress);

            return new { Path = path };
        });

        return command;
    }

    private static Command Import(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        db.Description = "The catalogue to create from the export. Asks before replacing one already there.";
        var json = new Argument<string>("json") { Description = "An export written by 'db export' or the GUI." }.TakesPath();

        var command = new Command("import", "Build a catalogue from a JSON export.") { json, db };

        command.SetJsonAction(services, async context =>
        {
            var source = Path.GetFullPath(context.ParseResult.GetRequiredValue(json));
            if (!File.Exists(source))
                throw CliException.NotFound($"There is no export at '{source}'.");

            var path = DatabaseFile.FullPath(context, db);
            if (string.Equals(path, source, StringComparison.OrdinalIgnoreCase))
                throw CliException.Usage("An export cannot be imported over itself. Give --db a catalogue to build.");

            DatabaseFile.ConfirmReplacing(context, path);

            // The export is read in full before the catalogue is replaced, so a bad one costs nothing
            try
            {
                await context.Service<IDatabaseTransferService>().ImportAsync(source, path, context.Progress);
            }
            catch (Exception e) when (e is JsonException or InvalidDataException)
            {
                throw new CliException(
                    ExitCode.Error, ErrorCodes.InvalidFile, $"'{source}' is not an export this version can read. {e.Message}");
            }

            var database = context.Service<IDatabaseService>();
            return new
            {
                Path = path,
                VolumeCount = await database.CountItemsAsync<VirtualVolumeRecord>(),
                FolderCount = await database.CountItemsAsync<RootFolderMetadata>(),
                RecordCount = await database.CountItemsAsync<FileRecord>(record => record.ParentId != null)
            };
        });

        return command;
    }
}
