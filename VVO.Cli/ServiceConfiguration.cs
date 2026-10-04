using Microsoft.Extensions.DependencyInjection;
using VVO.Core.Services;

namespace VVO.Cli;

/// <summary>
/// The Core services the GUI registers, without the undo stack: every invocation is a process of
/// its own, so there is nothing to undo across.
/// </summary>
public static class ServiceConfiguration
{
    /// <param name="configure">Replaces registrations, which is how the tests stand in for the terminal.</param>
    public static ServiceProvider ConfigureServices(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddSingleton<IFileScannerService, FileScannerService>();
        services.AddSingleton<IFolderCompareService, FolderCompareService>();
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IVirtualVolumeService, VirtualVolumeService>();
        services.AddSingleton<IDatabaseTransferService, DatabaseTransferService>();
        services.AddSingleton<ITerminal, ConsoleTerminal>();

        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }
}
