using System.CommandLine;
using VVO.Cli.Contract;
using VVO.Cli.Output;
using VVO.Core;
using VVO.Core.Services;

namespace VVO.Cli.Commands;

public static class VolumeCommands
{
    public static Command Create(IServiceProvider services)
    {
        return new Command("volume", "Virtual volumes: the groups folders are listed under.")
        {
            List(services),
            CreateVolume(services),
            Update(services),
            Delete(services)
        };
    }

    private static Command List(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var command = new Command("list", "List the virtual volumes, with how many folders each holds.") { db };

        command.SetJsonAction(services, async context =>
        {
            await DatabaseFile.OpenExistingAsync(context, db);

            var volumes = await context.Service<IVirtualVolumeService>().GetVirtualVolumesAsync();
            var folderCounts = await FolderCountsAsync(context);

            return volumes
                .OrderBy(volume => volume.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(volume => volume.Id)
                .Select(volume => VolumeDto.From(volume, folderCounts.GetValueOrDefault(volume.Id)))
                .ToList();
        }, TableFormat.Volumes);

        return command;
    }

    private static Command CreateVolume(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var name = new Argument<string>("name");
        var icon = IconOption($"Defaults to {IconKeys.DefaultVolume}.");
        var color = ColorOption();

        var command = new Command("create", "Create an empty virtual volume.") { name, db, icon, color };

        command.SetJsonAction(services, async context =>
        {
            var chosenIcon = Appearance.VolumeIcon(context.ParseResult.GetValue(icon) ?? IconKeys.DefaultVolume);
            var chosenColor = context.ParseResult.GetValue(color) is { } value ? Appearance.Color(value) : null;

            await DatabaseFile.OpenExistingAsync(context, db);

            var volume = await context.Service<IVirtualVolumeService>().CreateVirtualVolumeAsync(
                context.ParseResult.GetRequiredValue(name).Trim(), chosenIcon, chosenColor);

            return VolumeDto.From(volume, 0);
        });

        return command;
    }

    private static Command Update(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var id = new Argument<Guid>("id");
        var name = new Option<string>("--name");
        var icon = IconOption();
        var color = ColorOption();

        var command = new Command(
            "update",
            "Rename a virtual volume or change its icon or colour. What is not given is left as it is.")
        {
            id, db, name, icon, color
        };

        command.SetJsonAction(services, async context =>
        {
            var newName = context.ParseResult.GetValue(name);
            var newIcon = context.ParseResult.GetValue(icon);
            var newColor = context.ParseResult.GetValue(color);

            if (newName == null && newIcon == null && newColor == null)
                throw CliException.Usage("Give at least one of --name, --icon or --color.");

            var chosenIcon = newIcon != null ? Appearance.VolumeIcon(newIcon) : null;
            var chosenColor = newColor != null ? Appearance.Color(newColor) : null;

            await DatabaseFile.OpenExistingAsync(context, db);

            var volume = await Catalogue.VolumeAsync(context, context.ParseResult.GetValue(id));
            var updated = volume with
            {
                Name = newName?.Trim() ?? volume.Name,
                Icon = chosenIcon ?? volume.Icon,
                Color = newColor != null ? chosenColor : volume.Color
            };

            await context.Service<IVirtualVolumeService>()
                .UpdateVirtualVolumeAsync(updated.Id, updated.Name, updated.Icon, updated.Color);

            var folderCounts = await FolderCountsAsync(context);
            return VolumeDto.From(updated, folderCounts.GetValueOrDefault(updated.Id));
        });

        return command;
    }

    private static Command Delete(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var id = new Argument<Guid>("id");

        var command = new Command(
            "delete",
            "Delete a virtual volume and every folder in it. Asks the user to type 'delete' first.")
        {
            id, db
        };

        command.SetJsonAction(services, async context =>
        {
            await DatabaseFile.OpenExistingAsync(context, db);

            var volume = await Catalogue.VolumeAsync(context, context.ParseResult.GetValue(id));
            var folderCount = (await FolderCountsAsync(context)).GetValueOrDefault(volume.Id);

            Confirmation.RequireDeletion(context, DeletionWarnings.ForVirtualVolume(volume.Name, folderCount));

            await context.Service<IVirtualVolumeService>()
                .DeleteVirtualVolumeAsync(volume.Id, context.Progress, context.CancellationToken);

            return new { Deleted = VolumeDto.From(volume, folderCount) };
        });

        return command;
    }

    private static Option<string> IconOption(string? defaultNote = null)
    {
        var note = defaultNote != null ? $" {defaultNote}" : string.Empty;
        return new Option<string>("--icon")
        {
            Description = $"One of: {string.Join(", ", IconKeys.Volumes)}.{note}"
        };
    }

    private static Option<string> ColorOption() => new("--color")
    {
        Description = $"#RRGGBB or #AARRGGBB, or '{Appearance.NoColor}' for the application's own colour."
    };

    private static async Task<Dictionary<Guid, int>> FolderCountsAsync(CommandContext context) =>
        (await Catalogue.EntriesAsync(context))
            .GroupBy(entry => entry.VirtualVolumeId)
            .ToDictionary(group => group.Key, group => group.Count());
}
