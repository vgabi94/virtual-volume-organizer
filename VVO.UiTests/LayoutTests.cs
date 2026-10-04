using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using VVO.UI;

namespace VVO.UiTests;

/// <summary>
/// The layout switch: a class every window wears, so the styles under it reach dialogs as well
/// as the main window, including ones opened after the switch.
/// </summary>
public class LayoutTests : UiTestBase
{
    private static bool IsModern(Window window) => window.Classes.Contains(Layout.ModernClass);

    [AvaloniaFact]
    public void ASessionStartsInTheClassicLayout()
    {
        Assert.False(Layout.IsModern);
        Assert.False(IsModern(Shell));
    }

    [AvaloniaFact]
    public void SwitchingReachesEveryOpenWindowAndSwitchingBackLeavesNone()
    {
        var dialog = new Window();
        dialog.Show();

        Layout.Apply(true);

        Assert.True(IsModern(Shell));
        Assert.True(IsModern(dialog));

        Layout.Apply(false);

        Assert.False(IsModern(Shell));
        Assert.False(IsModern(dialog));
        dialog.Close();
    }

    [AvaloniaFact]
    public void AWindowOpenedAfterTheSwitchArrivesWearingIt()
    {
        Layout.Apply(true);

        var dialog = new Window();
        dialog.Show();

        Assert.True(IsModern(dialog));
        dialog.Close();
    }

    // A closed window is let go of, so the switch does not keep reaching for it
    [AvaloniaFact]
    public void AClosedWindowIsNoLongerSwitched()
    {
        var dialog = new Window();
        dialog.Show();
        dialog.Close();

        Layout.Apply(true);

        Assert.False(IsModern(dialog));
    }

    [AvaloniaFact]
    public void TheHookTakesTheSwitchInPlaceOfTheWindows()
    {
        var applied = new List<bool>();
        Layout.Applying = modern => applied.Add(modern);

        Layout.Apply(true);

        Assert.Equal([true], applied);
        Assert.False(Layout.IsModern);
        Assert.False(IsModern(Shell));
    }
}
