using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
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
        "TextControlBackground", "DialogBackground",
        "DiffAddedBackground", "DiffAddedSelectedBackground", "DiffAddedForeground",
        "DiffRemovedBackground", "DiffRemovedSelectedBackground", "DiffRemovedForeground",
        "DiffChangedBackground", "DiffChangedSelectedBackground", "DiffChangedForeground",
        "WindowBackground", "ChromeBackground", "SidebarBackground", "StartPageBackground",
        "ContentBackground", "ContentBorderBrush", "StatusBarBackground", "DividerBrush",
        "TextPrimaryBrush", "TextSecondaryBrush", "TextTertiaryBrush", "IconBrush",
        "HoverBackground", "PressedBackground", "SelectionBackground", "SelectionForeground",
        "RowSelectionBackground", "ControlBackground", "ControlBorderBrush",
        "AccentBrush", "AccentForegroundBrush", "DangerBrush", "ErrorTextBrush"
    ];

    /// <summary>
    /// The application's colours that are meant to read the same in both themes: the accent and
    /// what is drawn on it, which Fluent itself does not vary.
    /// </summary>
    private static readonly string[] SharedByBothThemes =
    [
        "SelectionBackground", "SelectionForeground", "AccentBrush", "AccentForegroundBrush"
    ];

    /// <summary>
    /// Each role and the brush it was drawn with before it had a name. The plain themes keep
    /// those colours, so naming them changed nothing on screen.
    /// </summary>
    private static readonly (string Role, string WasDrawnWith)[] Roles =
    [
        ("WindowBackground", "SystemRegionBrush"),
        ("ChromeBackground", "SystemControlBackgroundChromeMediumBrush"),
        ("SidebarBackground", "SystemControlBackgroundChromeMediumLowBrush"),
        ("StartPageBackground", "SystemControlBackgroundChromeMediumLowBrush"),
        ("ContentBorderBrush", "SystemControlForegroundBaseLowBrush"),
        ("StatusBarBackground", "SystemControlBackgroundChromeMediumBrush"),
        ("DividerBrush", "SystemControlForegroundBaseLowBrush"),
        ("TextPrimaryBrush", "SystemControlForegroundBaseHighBrush"),
        ("TextSecondaryBrush", "SystemControlForegroundBaseMediumBrush"),
        ("TextTertiaryBrush", "SystemControlForegroundBaseLowBrush"),
        ("IconBrush", "SystemControlForegroundBaseMediumBrush"),
        ("HoverBackground", "SystemControlHighlightListLowBrush"),
        ("PressedBackground", "SystemControlHighlightListMediumBrush"),
        ("SelectionBackground", "SystemControlHighlightListAccentLowBrush"),
        ("RowSelectionBackground", "SystemControlHighlightListLowBrush"),
        ("ControlBackground", "SystemControlBackgroundChromeMediumBrush"),
        ("ControlBorderBrush", "TextControlBorderBrush"),
        ("AccentBrush", "AccentButtonBackground"),
        ("AccentForegroundBrush", "AccentButtonForeground"),
        ("DangerBrush", "DiffRemovedForeground"),
        ("ErrorTextBrush", "SystemControlErrorTextForegroundBrush")
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

    // What a brush paints, solid or gradient, for telling two apart
    private static string PaintOf(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var found), $"missing '{key}'");

        return found switch
        {
            SolidColorBrush solid => solid.Color.ToString(),
            GradientBrush gradient => string.Join(" ", gradient.GradientStops.Select(stop => stop.Color)),
            _ => throw new InvalidOperationException($"'{key}' is neither solid nor a gradient")
        };
    }

    #region Both themes are complete

    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void EveryColourTheApplicationNamesResolvesInEveryTheme(bool dark, bool slate)
    {
        var variant = Theme.VariantFor(dark, slate);

        Assert.All(OwnBrushes.Concat(FluentBrushes).Concat(FileKinds), key =>
            Assert.True(Application.Current!.TryGetResource(key, variant, out _), $"missing '{key}'"));
    }

    // Slate inherits its plain theme, so whatever it does not name is that theme's
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASlateThemeFallsBackToItsPlainOneForEveryColourItDoesNotName(bool dark)
    {
        var plain = Theme.VariantFor(dark, slate: false);
        var slate = Theme.VariantFor(dark, slate: true);
        var named = NamedBy(slate);

        Assert.All(OwnBrushes.Concat(FluentBrushes).Where(key => !named.Contains(key)), key =>
            Assert.Equal(PaintOf(key, plain), PaintOf(key, slate)));
    }

    // A colour Slate names only to repeat the plain theme's is one that should not be there
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryColourASlateThemeNamesDiffersFromThePlainOne(bool dark)
    {
        var plain = Theme.VariantFor(dark, slate: false);
        var slate = Theme.VariantFor(dark, slate: true);

        Assert.All(NamedBy(slate).Where(key => Application.Current!.TryGetResource(key, plain, out _)), key =>
            Assert.NotEqual(PaintOf(key, plain), PaintOf(key, slate)));
    }

    // Two variants resolving to one colour is a value that was never given a second reading
    [AvaloniaFact]
    public void TheTwoThemesAgreeOnNothingTheApplicationDefinesForItself()
    {
        Assert.All(OwnBrushes.Except(SharedByBothThemes), key =>
            Assert.NotEqual(ColourOf(key, ThemeVariant.Dark), ColourOf(key, ThemeVariant.Light)));
    }

    // The surfaces both leave clear over the window's gradient are the same nothing
    [AvaloniaFact]
    public void TheTwoSlateThemesAgreeOnNothingButTheSurfacesTheyLeaveClear()
    {
        Assert.All(NamedBy(Theme.SlateDark), key =>
        {
            var dark = PaintOf(key, Theme.SlateDark);
            if (dark == "#00000000" && PaintOf(key, Theme.SlateLight) == "#00000000")
                return;

            Assert.NotEqual(dark, PaintOf(key, Theme.SlateLight));
        });
    }

    private static readonly string[] FileKinds =
    [
        "FolderSolidBrush", "FileImageSolidBrush", "FileAudioSolidBrush", "FileVideoSolidBrush",
        "FileZipperSolidBrush", "FilePdfSolidBrush", "FileWordSolidBrush", "FileExcelSolidBrush",
        "FileCsvSolidBrush", "FilePowerpointSolidBrush", "FileLinesSolidBrush", "FileCodeSolidBrush",
        "FileCogBrush", "FileEarmarkBinaryFillBrush", "CompactDiscSolidBrush", "DatabaseSolidBrush",
        "FileSolidBrush"
    ];

    /// <summary>The keys a variant's own dictionary names, as opposed to those it inherits.</summary>
    private static HashSet<string> NamedBy(ThemeVariant variant)
    {
        var theme = Application.Current!.Resources.MergedDictionaries
            .Select(dictionary => dictionary is ResourceInclude include ? include.Loaded : dictionary)
            .OfType<IResourceDictionary>()
            .Single(dictionary => dictionary.ThemeDictionaries.ContainsKey(variant));

        return ((IResourceDictionary)theme.ThemeDictionaries[variant]).Keys.Cast<string>().ToHashSet();
    }

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void EachRoleKeepsTheColourItWasDrawnInBeforeItHadAName(string variantName)
    {
        var variant = variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

        Assert.All(Roles, pair =>
        {
            var role = BrushOf(pair.Role, variant);
            var was = BrushOf(pair.WasDrawnWith, variant);

            Assert.True(role.Color == was.Color && Math.Abs(role.Opacity - was.Opacity) < 0.001,
                $"{pair.Role} is {role.Color} at {role.Opacity}, {pair.WasDrawnWith} is {was.Color} at {was.Opacity}");
        });
    }

    private static ISolidColorBrush BrushOf(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var found), $"missing '{key}'");

        return Assert.IsAssignableFrom<ISolidColorBrush>(found);
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

    #region The window

    // Painted by the window itself, so it moves with the window rather than staying put on the desktop
    [AvaloniaTheory]
    [InlineData(true, "#26282E", "#1B1C20")]
    [InlineData(false, "#F7F8FB", "#E8EBF1")]
    public void ASlateWindowIsPaintedWithItsThemesGradient(bool dark, string from, string to)
    {
        var window = new Window { Classes = { "AppWindow" }, RequestedThemeVariant = Theme.VariantFor(dark, slate: true) };
        window.Show();

        var gradient = Assert.IsType<LinearGradientBrush>(window.Background);
        Assert.Equal([Color.Parse(from), Color.Parse(to)], gradient.GradientStops.Select(stop => stop.Color));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void NoThemeAsksTheSystemToDrawBehindTheWindow(bool dark, bool slate)
    {
        Shell.RequestedThemeVariant = Theme.VariantFor(dark, slate);

        Assert.Empty(Shell.TransparencyLevelHint);
    }

    // Choosing Slate from Options happens with the window already open
    [AvaloniaFact]
    public void AWindowAlreadyOpenWhenSlateIsChosenIsRepaintedInIt()
    {
        var window = new Window { Classes = { "AppWindow" }, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();

        window.RequestedThemeVariant = Theme.SlateDark;

        Assert.IsType<LinearGradientBrush>(window.Background);
        window.Close();
    }

    // Options is closed by then, but the Compare window and other dialogs may still be up
    [AvaloniaFact]
    public void ThemeAndLayoutChosenWithWindowsOpenReachThemWithoutARestart()
    {
        var application = Application.Current!;
        var before = application.RequestedThemeVariant;
        var compare = new CompareResultsView
        {
            DataContext = new CompareResultsViewModel("Source", @"D:\Source", "Target", @"E:\Target", [])
        };
        var dialog = new NameDialogView { DataContext = new NameDialogViewModel("Name", "Save", "Code") };
        compare.Show();
        dialog.Show();

        try
        {
            Theme.Apply(dark: false, slate: true);
            Layout.Apply(true);

            Assert.All(new Window[] { compare, dialog }, window =>
            {
                Assert.Equal(Theme.SlateLight, window.ActualThemeVariant);
                Assert.Contains(Layout.ModernClass, window.Classes);
            });
            Assert.Equal(Color.Parse("#F7F8FB"), Assert.IsType<LinearGradientBrush>(compare.Background).GradientStops[0].Color);
        }
        finally
        {
            application.RequestedThemeVariant = before;
            compare.Close();
            dialog.Close();
        }
    }

    #endregion

    #region Choosing one

    [AvaloniaTheory]
    [InlineData(true, false, "Dark")]
    [InlineData(false, false, "Light")]
    [InlineData(true, true, "SlateDark")]
    [InlineData(false, true, "SlateLight")]
    public void ApplyingPutsOnTheVariantForEachChoice(bool dark, bool slate, string expected)
    {
        var application = Application.Current!;
        var before = application.RequestedThemeVariant;

        try
        {
            Theme.Apply(dark, slate);

            Assert.Equal(expected, application.ActualThemeVariant.Key);
            Assert.Equal(dark ? ThemeVariant.Dark : ThemeVariant.Light,
                application.ActualThemeVariant.InheritVariant ?? application.ActualThemeVariant);
        }
        finally
        {
            application.RequestedThemeVariant = before;
        }
    }

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
        Theme.Applying = (dark, _) => applied.Add(dark);

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
        Theme.Applying = (dark, _) => applied.Add(dark);

        new OptionsDialogViewModel(Settings) { DarkTheme = true }.Apply();

        Assert.Equal([true], applied);
        Assert.True(Settings.IsDarkTheme);
    }

    [AvaloniaFact]
    public void TheOptionsDialogStartsFromTheStoredSlateAndLayout()
    {
        Settings.SetSlateTheme(true);
        Settings.SetModernLayout(false);

        var options = new OptionsDialogViewModel(Settings);

        Assert.True(options.SlateTheme);
        Assert.False(options.ModernLayout);
    }

    [AvaloniaFact]
    public void SavingTheOptionsPutsOnAndRemembersTheSlateAndLayoutChosen()
    {
        var themes = new List<(bool Dark, bool Slate)>();
        var layouts = new List<bool>();
        Theme.Applying = (dark, slate) => themes.Add((dark, slate));
        Layout.Applying = modern => layouts.Add(modern);

        new OptionsDialogViewModel(Settings) { SlateTheme = true, ModernLayout = false }.Apply();

        Assert.Equal([(true, true)], themes);
        Assert.Equal([false], layouts);
        var reopened = new Settings(SettingsPath);
        Assert.True(reopened.IsSlateTheme);
        Assert.False(reopened.IsModernLayout);
    }

    [AvaloniaFact]
    public void CancellingTheOptionsDialogLeavesSlateAndTheLayoutAlone()
    {
        var themes = new List<(bool Dark, bool Slate)>();
        var layouts = new List<bool>();
        Theme.Applying = (dark, slate) => themes.Add((dark, slate));
        Layout.Applying = modern => layouts.Add(modern);

        _ = new OptionsDialogViewModel(Settings) { SlateTheme = true, ModernLayout = false };

        Assert.Empty(themes);
        Assert.Empty(layouts);
        Assert.False(Settings.IsSlateTheme);
        Assert.True(Settings.IsModernLayout);
    }

    [AvaloniaFact]
    public void CancellingTheOptionsDialogLeavesTheThemeAlone()
    {
        var applied = new List<bool>();
        Theme.Applying = (dark, _) => applied.Add(dark);

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
