using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using VVO.UI.ViewModels;
using VVO.UI.Views;

namespace VVO.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var serviceProvider = ServiceConfiguration.ConfigureServices();

            // Before the window is built, so it is never painted in one theme and then repainted
            Theme.Apply(serviceProvider.GetRequiredService<Settings>().IsDarkTheme);

            var mainWindowVm = serviceProvider.GetRequiredService<MainWindowViewModel>();

            var window = new MainWindowView
            {
                DataContext = mainWindowVm,
            };

            desktop.MainWindow = window;

            var catalogue = StartupCatalogue.From(desktop.Args);
            if (catalogue != null)
            {
                // Waited for rather than opened here: a catalogue that will not open says so in
                // a dialog, and a dialog needs a window to stand on
                window.Opened += async (_, _) =>
                    await mainWindowVm.StartPageViewModel.OpenAtStartupAsync(catalogue);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}