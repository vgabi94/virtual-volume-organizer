using System;
using Microsoft.Extensions.DependencyInjection;
using VVO.Core.Services;
using VVO.UI.ViewModels;

namespace VVO.UI;

public static class ServiceConfiguration
{
    public static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IFileScannerService, FileScannerService>();
        services.AddSingleton<IFolderCompareService, FolderCompareService>();
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IVirtualVolumeService, VirtualVolumeService>();
        services.AddSingleton<IDatabaseTransferService, DatabaseTransferService>();
        services.AddSingleton<IUndoService, UndoService>();
        services.AddSingleton<Settings>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<SidebarViewModel>();
        services.AddSingleton<VolumeExplorerViewModel>();
        services.AddSingleton<StartPageViewModel>();

        return services.BuildServiceProvider();
    }
}