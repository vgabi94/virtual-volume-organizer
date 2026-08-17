using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(VVO.UiTests.HeadlessTestApp))]

// One headless session owns a UI thread for the whole assembly, and every test drives it
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace VVO.UiTests;

/// <summary>
/// Holds the builder the headless session starts from. The application under test is the real
/// one, so its styles, themes and icon resources are the ones these tests exercise.
/// </summary>
public static class HeadlessTestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<VVO.UI.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
