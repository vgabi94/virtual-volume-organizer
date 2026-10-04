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
        "WindowBackground", "WindowFallbackBackground", "ChromeBackground", "SidebarBackground", "StartPageBackground",
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

    #region Both themes are complete

    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void EveryColourTheApplicationNamesResolvesInEveryTheme(bool dark, bool mica)
    {
        var variant = Theme.VariantFor(dark, mica);

        Assert.All(OwnBrushes.Concat(FluentBrushes).Concat(FileKinds), key =>
            Assert.True(Application.Current!.TryGetResource(key, variant, out _), $"missing '{key}'"));
    }

    // Mica is drawn over the plain theme it inherits, so whatever it does not name is that theme's
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void AMicaThemeFallsBackToItsPlainOneForEveryColourItDoesNotName(bool dark)
    {
        var plain = Theme.VariantFor(dark, mica: false);
        var mica = Theme.VariantFor(dark, mica: true);
        var named = NamedBy(mica);

        Assert.All(OwnBrushes.Concat(FluentBrushes).Where(key => !named.Contains(key)), key =>
            Assert.Equal(ColourOf(key, plain), ColourOf(key, mica)));
    }

    // A colour Mica names only to repeat the plain theme's is one that should not be there
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryColourAMicaThemeNamesDiffersFromThePlainOne(bool dark)
    {
        var plain = Theme.VariantFor(dark, mica: false);
        var mica = Theme.VariantFor(dark, mica: true);

        Assert.All(NamedBy(mica).Where(key => Application.Current!.TryGetResource(key, plain, out _)), key =>
            Assert.NotEqual(ColourOf(key, plain), ColourOf(key, mica)));
    }

    // Two variants resolving to one colour is a value that was never given a second reading
    [AvaloniaFact]
    public void TheTwoThemesAgreeOnNothingTheApplicationDefinesForItself()
    {
        Assert.All(OwnBrushes.Except(SharedByBothThemes), key =>
            Assert.NotEqual(ColourOf(key, ThemeVariant.Dark), ColourOf(key, ThemeVariant.Light)));
    }

    // The surfaces both leave clear for the backdrop are the same nothing, and that is the point
    [AvaloniaFact]
    public void TheTwoMicaThemesAgreeOnNothingButTheSurfacesTheyLeaveClear()
    {
        Assert.All(NamedBy(Theme.MicaDark), key =>
        {
            var dark = ColourOf(key, Theme.MicaDark);
            if (dark.A == 0 && ColourOf(key, Theme.MicaLight).A == 0)
                return;

            Assert.NotEqual(dark, ColourOf(key, Theme.MicaLight));
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

    #region The backdrop

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void AWindowAsksForMicaUnderAMicaTheme(bool dark)
    {
        Shell.RequestedThemeVariant = Theme.VariantFor(dark, mica: true);

        Assert.Equal(
            [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None],
            Shell.TransparencyLevelHint);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void AWindowAsksForNothingUnderAPlainTheme(bool dark)
    {
        Shell.RequestedThemeVariant = Theme.VariantFor(dark, mica: false);

        Assert.Empty(Shell.TransparencyLevelHint);
    }

    // The headless platform draws no Mica, which is what Windows 10 or a remote session amounts to
    [AvaloniaTheory]
    [InlineData(true, "#202227")]
    [InlineData(false, "#F3F4F7")]
    public void WhereNoMicaIsDrawnTheWindowIsPaintedASolidColourOfItsTheme(bool dark, string expected)
    {
        var window = new Window { Classes = { "AppWindow" }, RequestedThemeVariant = Theme.VariantFor(dark, mica: true) };
        window.Show();

        Assert.Equal(WindowTransparencyLevel.None, window.ActualTransparencyLevel);
        Assert.Contains(Theme.OpaqueClass, window.Classes);
        Assert.Equal(Color.Parse(expected), Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color);
        window.Close();
    }

    // Choosing Mica from Options happens with the window already open
    [AvaloniaFact]
    public void AWindowAlreadyOpenWhenMicaIsChosenIsPaintedInMicasSolidColour()
    {
        var window = new Window { Classes = { "AppWindow" }, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();

        window.RequestedThemeVariant = Theme.MicaDark;

        Assert.Equal(Color.Parse("#202227"), Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color);
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
            Theme.Apply(dark: false, mica: true);
            Layout.Apply(true);

            Assert.All(new Window[] { compare, dialog }, window =>
            {
                Assert.Equal(Theme.MicaLight, window.ActualThemeVariant);
                Assert.Contains(Layout.ModernClass, window.Classes);
                Assert.NotEmpty(window.TransparencyLevelHint);
            });
            Assert.Equal(Color.Parse("#F3F4F7"), Assert.IsAssignableFrom<ISolidColorBrush>(compare.Background).Color);
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
    [InlineData(true, true, "MicaDark")]
    [InlineData(false, true, "MicaLight")]
    public void ApplyingPutsOnTheVariantForEachChoice(bool dark, bool mica, string expected)
    {
        var application = Application.Current!;
        var before = application.RequestedThemeVariant;

        try
        {
            Theme.Apply(dark, mica);

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
    public void TheOptionsDialogStartsFromTheStoredMicaAndLayout()
    {
        Settings.SetMicaTheme(true);
        Settings.SetModernLayout(false);

        var options = new OptionsDialogViewModel(Settings);

        Assert.True(options.MicaTheme);
        Assert.False(options.ModernLayout);
    }

    [AvaloniaFact]
    public void SavingTheOptionsPutsOnAndRemembersTheMicaAndLayoutChosen()
    {
        var themes = new List<(bool Dark, bool Mica)>();
        var layouts = new List<bool>();
        Theme.Applying = (dark, mica) => themes.Add((dark, mica));
        Layout.Applying = modern => layouts.Add(modern);

        new OptionsDialogViewModel(Settings) { MicaTheme = true, ModernLayout = false }.Apply();

        Assert.Equal([(true, true)], themes);
        Assert.Equal([false], layouts);
        var reopened = new Settings(SettingsPath);
        Assert.True(reopened.IsMicaTheme);
        Assert.False(reopened.IsModernLayout);
    }

    [AvaloniaFact]
    public void CancellingTheOptionsDialogLeavesMicaAndTheLayoutAlone()
    {
        var themes = new List<(bool Dark, bool Mica)>();
        var layouts = new List<bool>();
        Theme.Applying = (dark, mica) => themes.Add((dark, mica));
        Layout.Applying = modern => layouts.Add(modern);

        _ = new OptionsDialogViewModel(Settings) { MicaTheme = true, ModernLayout = false };

        Assert.Empty(themes);
        Assert.Empty(layouts);
        Assert.False(Settings.IsMicaTheme);
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
