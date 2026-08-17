using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using VVO.Core.Services;
using VVO.UI;
using VVO.UI.ViewModels;

namespace VVO.UiTests;

/// <summary>
/// What the application is put together from. Nothing here is reached by exercising a command,
/// so a service left unregistered would only show up as a crash on startup.
/// </summary>
public class CompositionTests : IDisposable
{
    private readonly ServiceProvider _services;

    public CompositionTests()
    {
        _services = (ServiceProvider)ServiceConfiguration.ConfigureServices();
    }

    public void Dispose()
    {
        // The view models registered themselves for broadcasts on being built, and the messenger
        // outlives this test; left listening, they would answer a later test's broadcast by
        // reaching for a database they were never given.
        foreach (var viewModel in new object[]
                 {
                     _services.GetRequiredService<MainWindowViewModel>(),
                     _services.GetRequiredService<SidebarViewModel>(),
                     _services.GetRequiredService<VolumeExplorerViewModel>(),
                     _services.GetRequiredService<StartPageViewModel>()
                 })
        {
            WeakReferenceMessenger.Default.UnregisterAll(viewModel);
        }

        _services.Dispose();
        GC.SuppressFinalize(this);
    }

    [AvaloniaFact]
    public void EveryServiceTheWindowAsksForCanBeResolved()
    {
        Assert.NotNull(_services.GetRequiredService<IFileScannerService>());
        Assert.NotNull(_services.GetRequiredService<IFolderCompareService>());
        Assert.NotNull(_services.GetRequiredService<IDatabaseService>());
        Assert.NotNull(_services.GetRequiredService<IVirtualVolumeService>());
        Assert.NotNull(_services.GetRequiredService<IDatabaseTransferService>());
        Assert.NotNull(_services.GetRequiredService<IUndoService>());
        Assert.NotNull(_services.GetRequiredService<Settings>());
    }

    [AvaloniaFact]
    public void TheMainWindowIsBuiltFromTheViewModelsTheRestOfTheWindowShares()
    {
        var mainWindow = _services.GetRequiredService<MainWindowViewModel>();

        // Held as singletons, so a message one of them broadcasts reaches the same instance the
        // window is showing rather than a second copy of it
        Assert.Same(_services.GetRequiredService<SidebarViewModel>(), mainWindow.SidebarViewModel);
        Assert.Same(_services.GetRequiredService<VolumeExplorerViewModel>(), mainWindow.VolumeExplorerViewModel);
        Assert.Same(_services.GetRequiredService<StartPageViewModel>(), mainWindow.StartPageViewModel);
        Assert.Same(_services.GetRequiredService<MainWindowViewModel>(), mainWindow);
    }

    // The same builder the designer loads and the executable starts from, so a package left out
    // of it would only show up as an unstyled or fontless window on the first real run
    [AvaloniaFact]
    public void TheApplicationIsBuiltFromTheAppTheRestOfTheseTestsRun()
    {
        var builder = Program.BuildAvaloniaApp();

        Assert.Equal(typeof(App), builder.ApplicationType);
    }

    [AvaloniaFact]
    public void OneDatabaseAndOneUndoStackAreSharedByEverythingThatUsesThem()
    {
        _services.GetRequiredService<MainWindowViewModel>();

        Assert.Same(_services.GetRequiredService<IUndoService>(), _services.GetRequiredService<IUndoService>());
        Assert.Same(_services.GetRequiredService<IDatabaseService>(), _services.GetRequiredService<IDatabaseService>());
        Assert.Same(_services.GetRequiredService<Settings>(), _services.GetRequiredService<Settings>());
    }
}
