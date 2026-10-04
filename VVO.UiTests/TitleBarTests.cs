using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VVO.UI;
using VVO.UI.Views;

namespace VVO.UiTests;

/// <summary>
/// The Modern layout draws the menu row as the main window's title bar; the Classic one leaves the
/// system's title bar alone.
/// </summary>
public class TitleBarTests : UiTestBase
{
    private const string AppTitle = "Virtual Volume Organizer";

    private MainWindowView Opened()
    {
        var window = new MainWindowView { DataContext = NewMainWindow() };
        window.Show();
        Pump();

        return window;
    }

    private static T Classed<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.Classes.Contains(name));

    [AvaloniaFact]
    public void TheModernLayoutExtendsTheMainWindowIntoItsTitleBar()
    {
        Layout.Apply(true);
        var window = Opened();

        Assert.True(window.ExtendClientAreaToDecorationsHint);
        Assert.True(Classed<Image>(window, "TitleBarIcon").IsEffectivelyVisible);
        Assert.Equal(AppTitle, window.Title);
        window.Close();
    }

    [AvaloniaFact]
    public void TheClassicLayoutLeavesTheSystemTitleBar()
    {
        var window = Opened();

        Assert.False(window.ExtendClientAreaToDecorationsHint);
        Assert.False(Classed<Image>(window, "TitleBarIcon").IsEffectivelyVisible);
        Assert.Equal(AppTitle, window.Title);
        window.Close();
    }

    [AvaloniaFact]
    public void SwitchingLayoutsWhileTheWindowIsOpenTurnsTheTitleBarOnAndOff()
    {
        var window = Opened();

        Layout.Apply(true);
        Assert.True(window.ExtendClientAreaToDecorationsHint);

        Layout.Apply(false);
        Assert.False(window.ExtendClientAreaToDecorationsHint);
        window.Close();
    }

    // Dialogs and the Compare window keep their own title, which says what they are for
    [AvaloniaFact]
    public void NoOtherWindowIsExtended()
    {
        Layout.Apply(true);
        var dialog = new CompareResultsView();
        dialog.Show();

        Assert.False(dialog.ExtendClientAreaToDecorationsHint);
        dialog.Close();
    }

    // The title would be written over the menu; the theme keeps Avalonia's caption buttons
    [AvaloniaFact]
    public void TheModernTitleBarIsDrawnWithoutTheWindowsTitle()
    {
        var window = Opened();

        var theme = Assert.IsType<ControlTheme>(window.WindowDecorationsTheme);
        Assert.Equal(typeof(WindowDrawnDecorations), theme.TargetType);
        Assert.NotNull(theme.BasedOn);
        Assert.Contains(theme.Children.OfType<Style>(), style => style.Selector!.ToString().Contains("PART_TitleTextPanel"));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Maximized)]
    public void EscLeavesFullScreenForWhereTheWindowWasBefore(WindowState before)
    {
        var window = Opened();
        window.WindowState = before;
        window.WindowState = WindowState.FullScreen;

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

        Assert.Equal(before, window.WindowState);
        window.Close();
    }

    [AvaloniaFact]
    public void EscOutsideFullScreenLeavesTheWindowAlone()
    {
        var window = Opened();
        window.WindowState = WindowState.Maximized;

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

        Assert.Equal(WindowState.Maximized, window.WindowState);
        window.Close();
    }

    [AvaloniaFact]
    public void TheEmptyPartOfTheMenuRowMovesTheWindow()
    {
        Layout.Apply(true);
        var window = Opened();

        Assert.Equal(WindowDecorationsElementRole.TitleBar,
            WindowDecorationProperties.GetElementRole(Classed<Border>(window, "TitleBarDrag")));
        window.Close();
    }
}
