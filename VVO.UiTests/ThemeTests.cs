using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VVO.UI;
using VVO.UI.ViewModels;
using VVO.UI.Views;

namespace VVO.UiTests;

/// <summary>
/// The two themes and the setting that chooses between them. Every colour the application names
/// has to resolve under both, or a variant lands on screen with holes in it.
/// </summary>
public class ThemeTests : UiTestBase
{
    /// <summary>Every colour the application defines for itself rather than taking from Fluent.</summary>
    private static readonly string[] OwnBrushes =
    [
        "TextControlBackground", "ExplorerBackground", "DialogBackground",
        "DiffAddedBackground", "DiffAddedSelectedBackground", "DiffAddedForeground",
        "DiffRemovedBackground", "DiffRemovedSelectedBackground", "DiffRemovedForeground",
        "DiffChangedBackground", "DiffChangedSelectedBackground", "DiffChangedForeground"
    ];

    /// <summary>Colours taken from Fluent, which the light palette has to answer for too.</summary>
    private static readonly string[] FluentBrushes =
    [
        "SystemRegionBrush", "SystemControlBackgroundChromeMediumBrush",
        "SystemControlBackgroundChromeMediumLowBrush", "SystemControlForegroundBaseHighBrush",
        "SystemControlForegroundBaseMediumBrush", "SystemControlForegroundBaseLowBrush",
        "SystemControlHighlightListLowBrush", "SystemControlHighlightListMediumBrush",
        "SystemControlHighlightListAccentLowBrush", "TextControlBorderBrush"
    ];

    private static Color ColourOf(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var found), $"missing '{key}'");

        return Assert.IsType<SolidColorBrush>(found).Color;
    }

    #region Both themes are complete

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void EveryColourTheApplicationNamesResolvesInBothThemes(string variantName)
    {
        var variant = variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

        Assert.All(OwnBrushes.Concat(FluentBrushes), key =>
            Assert.True(Application.Current!.TryGetResource(key, variant, out _), $"missing '{key}'"));
    }

    // Two variants resolving to one colour is a value that was never given a second reading
    [AvaloniaFact]
    public void TheTwoThemesAgreeOnNothingTheApplicationDefinesForItself()
    {
        Assert.All(OwnBrushes, key =>
            Assert.NotEqual(ColourOf(key, ThemeVariant.Dark), ColourOf(key, ThemeVariant.Light)));
    }

    // Light on light or dark on dark is unreadable however elegant the hue
    [AvaloniaFact]
    public void EachThemePutsItsTextTheOppositeWayRoundFromItsGround()
    {
        var darkGround = ColourOf("SystemRegionBrush", ThemeVariant.Dark);
        var darkText = ColourOf("SystemControlForegroundBaseHighBrush", ThemeVariant.Dark);
        var lightGround = ColourOf("SystemRegionBrush", ThemeVariant.Light);
        var lightText = ColourOf("SystemControlForegroundBaseHighBrush", ThemeVariant.Light);

        Assert.True(Luminance(darkText) > Luminance(darkGround), "dark theme is not light on dark");
        Assert.True(Luminance(lightText) < Luminance(lightGround), "light theme is not dark on light");
    }

    // A page of pure white under pure black is what the light theme exists to avoid
    [AvaloniaFact]
    public void TheLightThemeIsPaperAndInkRatherThanWhiteAndBlack()
    {
        var ground = ColourOf("SystemRegionBrush", ThemeVariant.Light);
        var text = ColourOf("SystemControlForegroundBaseHighBrush", ThemeVariant.Light);

        Assert.True(Luminance(ground) is > 0.9 and < 1.0, $"ground is {ground}");
        Assert.True(Luminance(text) is > 0.0 and < 0.25, $"text is {text}");

        // and still far enough apart to read comfortably
        Assert.True(Luminance(ground) - Luminance(text) > 0.7);
    }

    private static double Luminance(Color colour) =>
        (0.299 * colour.R + 0.587 * colour.G + 0.114 * colour.B) / 255.0;

    #endregion

    #region Choosing one

    [AvaloniaFact]
    public void TheApplicationOpensDarkUntilItIsToldOtherwise()
    {
        Assert.True(Settings.IsDarkTheme);
        Assert.True(new OptionsDialogViewModel(Settings).DarkTheme);
    }

    // Written before there was a theme to choose, so it carries no answer for one
    [AvaloniaFact]
    public void ASettingsFileFromBeforeTheThemeExistedReadsAsDark()
    {
        var path = TempPath("json");
        File.WriteAllText(path, """{ "RecentFiles": [], "MaxRecentFiles": 7 }""");

        Assert.True(new Settings(path).IsDarkTheme);
    }

    [AvaloniaFact]
    public void ChoosingTheLightThemePutsItOnAndRemembersIt()
    {
        var applied = new List<bool>();
        Theme.Applying = dark => applied.Add(dark);

        var options = new OptionsDialogViewModel(Settings) { DarkTheme = false };
        options.Apply();

        Assert.Equal([false], applied);
        Assert.False(Settings.IsDarkTheme);
        Assert.False(new Settings(SettingsPath).IsDarkTheme);
    }

    [AvaloniaFact]
    public void ChoosingTheDarkThemeAgainPutsItBack()
    {
        Settings.SetDarkTheme(false);

        var applied = new List<bool>();
        Theme.Applying = dark => applied.Add(dark);

        new OptionsDialogViewModel(Settings) { DarkTheme = true }.Apply();

        Assert.Equal([true], applied);
        Assert.True(Settings.IsDarkTheme);
    }

    [AvaloniaFact]
    public void CancellingTheOptionsDialogLeavesTheThemeAlone()
    {
        var applied = new List<bool>();
        Theme.Applying = dark => applied.Add(dark);

        // Filled in and never applied, which is what pressing Cancel amounts to
        _ = new OptionsDialogViewModel(Settings) { DarkTheme = false };

        Assert.Empty(applied);
        Assert.True(Settings.IsDarkTheme);
    }

    #endregion

    #region On screen

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void TheMainWindowBuildsUnderEitherTheme(string variantName)
    {
        var window = new MainWindowView
        {
            DataContext = NewMainWindow(),
            RequestedThemeVariant = variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light
        };

        window.Show();
        Pump();

        Assert.NotEmpty(window.GetVisualDescendants().OfType<Menu>());
        window.Close();
    }

    [AvaloniaFact]
    public void TheOptionsDialogOffersTheThemeAsItsFirstChoice()
    {
        var window = new OptionsDialogView { DataContext = new OptionsDialogViewModel(Settings) };
        window.Show();
        Pump();

        var first = window.GetVisualDescendants().OfType<CheckBox>().First();

        Assert.Equal("Dark theme", first.Content);
        Assert.True(first.IsChecked);
        window.Close();
    }

    #endregion
}
