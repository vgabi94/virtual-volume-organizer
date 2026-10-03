using Microsoft.Extensions.DependencyInjection;
using VVO.Core.Services;

namespace VVO.Cli;

/// <summary>
/// The Core services the GUI registers, without the undo stack: every invocation is a process of
/// its own, so there is nothing to undo across.
/// </summary>
public static class ServiceConfiguration
{
    public static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IFileScannerService, FileScannerService>();
        services.AddSingleton<IFolderCompareService, FolderCompareService>();
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IVirtualVolumeService, VirtualVolumeService>();
        services.AddSingleton<IDatabaseTransferService, DatabaseTransferService>();

        return services.BuildServiceProvider();
    }
}
