using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using VVO.Core.Services;
using VVO.UI;
using VVO.UI.ViewModels;
using VVO.UI.Views;

namespace VVO.UiTests;

/// <summary>
/// What the application does on being started: build the window and hand it everything it
/// needs. Nothing in the running window reaches this, so a service or a view model wired up
/// wrongly here would only show up as a blank window on the first real run.
/// </summary>
public class StartupTests
{
    // A lifetime of its own rather than the one this test session runs under, which is headless
    // and has none: what is under test is what the application makes of being handed one.
    [AvaloniaFact]
    public void StartingUpBuildsTheMainWindowOverTheMainWindowViewModel()
    {
        var app = new App { ApplicationLifetime = new ClassicDesktopStyleApplicationLifetime() };

        app.OnFrameworkInitializationCompleted();

        var lifetime = (IClassicDesktopStyleApplicationLifetime)app.ApplicationLifetime!;
        var window = Assert.IsType<MainWindowView>(lifetime.MainWindow);
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);

        Assert.NotNull(viewModel.SidebarViewModel);
        Assert.NotNull(viewModel.VolumeExplorerViewModel);
        Assert.NotNull(viewModel.StartPageViewModel);

        window.Close();

        // Built over a database nothing has opened, and the messenger outlives this test; left
        // listening, they would answer a later test's broadcast by reaching for it
        foreach (var listening in new object[]
                 {
                     viewModel, viewModel.SidebarViewModel,
                     viewModel.VolumeExplorerViewModel, viewModel.StartPageViewModel
                 })
        {
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(listening);
        }
    }

    [AvaloniaFact]
    public void StartingUpWithNoDesktopToShowOnBuildsNoWindow()
    {
        var app = new App();

        app.OnFrameworkInitializationCompleted();

        Assert.Null(app.ApplicationLifetime);
    }

    #region Started on a catalogue

    /// <summary>
    /// Starts the application the way the shell does when a .vvo is double-clicked, and hands
    /// back the window's view model once the window is up.
    /// </summary>
    private static (MainWindowViewModel ViewModel, MainWindowView Window) StartedOn(params string[] args)
    {
        var app = new App
        {
            ApplicationLifetime = new ClassicDesktopStyleApplicationLifetime { Args = args }
        };

        app.OnFrameworkInitializationCompleted();

        var lifetime = (IClassicDesktopStyleApplicationLifetime)app.ApplicationLifetime!;
        var window = (MainWindowView)lifetime.MainWindow!;

        // Opening the catalogue waits on the window, which is what gives a failure a dialog to
        // land on. Nothing runs until it is shown.
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return ((MainWindowViewModel)window.DataContext!, window);
    }

    private static async Task Settled(Func<bool> done)
    {
        for (var attempt = 0; attempt < 200 && !done(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }
    }

    private static void LetGoOf(MainWindowViewModel viewModel, MainWindowView window)
    {
        window.Close();

        foreach (var listening in new object[]
                 {
                     viewModel, viewModel.SidebarViewModel,
                     viewModel.VolumeExplorerViewModel, viewModel.StartPageViewModel
                 })
        {
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(listening);
        }

        Dialogs.Reset();
    }

    [AvaloniaFact]
    public async Task StartingOnACatalogueOpensItInsteadOfShowingTheStartPage()
    {
        var catalogue = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.vvo");
        await new DatabaseService().EnsureDatabaseReadyAsync(catalogue);

        var told = new List<string>();
        Dialogs.Told = (title, _) => { told.Add(title); return Task.CompletedTask; };

        var (viewModel, window) = StartedOn(catalogue);

        try
        {
            await Settled(() => !viewModel.IsStartPageVisible);

            Assert.False(viewModel.IsStartPageVisible);
            Assert.Empty(told);
        }
        finally
        {
            LetGoOf(viewModel, window);
            try { File.Delete(catalogue); } catch { }
        }
    }

    [AvaloniaFact]
    public void StartingOnNothingLeavesTheStartPageUp()
    {
        var (viewModel, window) = StartedOn();

        try
        {
            Assert.True(viewModel.IsStartPageVisible);
        }
        finally
        {
            LetGoOf(viewModel, window);
        }
    }

    #endregion
}
