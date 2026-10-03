using System.CommandLine;
using VVO.Cli.Contract;
using VVO.Core.Services;

namespace VVO.Cli.Commands;

public static class VolumeCommands
{
    public static Command Create(IServiceProvider services)
    {
        return new Command("volume", "Virtual volumes: the groups folders are listed under.")
        {
            List(services)
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
            var folderCounts = (await Catalogue.EntriesAsync(context))
                .GroupBy(entry => entry.VirtualVolumeId)
                .ToDictionary(group => group.Key, group => group.Count());

            return volumes
                .OrderBy(volume => volume.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(volume => volume.Id)
                .Select(volume => VolumeDto.From(volume, folderCounts.GetValueOrDefault(volume.Id)))
                .ToList();
        });

        return command;
    }
}
