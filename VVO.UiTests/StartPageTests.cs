using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using VVO.Core.Models;
using VVO.UI;
using VVO.UI.Messages;
using VVO.UI.ViewModels;
using VVO.UI.Views;

namespace VVO.UiTests;

/// <summary>
/// The page shown before any database is open, which offers the same two ways in as the menu
/// bar does and is the only place the list of recent databases is shown.
/// </summary>
public class StartPageTests : UiTestBase
{
    private StartPageViewModel StartPage { get; }

    public StartPageTests()
    {
        StartPage = NewStartPage();
    }

    #region Creating a database

    [AvaloniaFact]
    public async Task ANewDatabaseIsCataloguedUnderTheNameTheUserGaveIt()
    {
        var target = TempPath("vvo");

        AnswerDialogs<NewDatabaseDialogViewModel>(dialog =>
        {
            dialog.CustomName = "Discs";
            dialog.DatabasePath = target;
        });

        await StartPage.NewDatabaseCommand.ExecuteAsync(Shell);

        Assert.Equal(target, Database.DbPath);
        Assert.Equal("Discs", (await Database.ReadItemsAsync<DatabaseMetadata>()).Single().Name);
    }

    [AvaloniaFact]
    public async Task ANewDatabaseIsRememberedAndPutsTheStartPageAway()
    {
        var target = TempPath("vvo");
        using var hidden = new MessageProbe<HideStartPage>();
        using var ready = new MessageProbe<DatabaseReady>();

        AnswerDialogs<NewDatabaseDialogViewModel>(dialog =>
        {
            dialog.CustomName = "Discs";
            dialog.DatabasePath = target;
        });

        await StartPage.NewDatabaseCommand.ExecuteAsync(Shell);

        Assert.Contains(target, Settings.Data.RecentFiles);
        Assert.Equal(target, StartPage.RecentDatabases[0].Path);
        Assert.Single(hidden.All);
        Assert.Single(ready.All);
    }

    [AvaloniaFact]
    public async Task CancellingTheNewDatabaseDialogCreatesNothing()
    {
        var before = Database.DbPath;
        AnswerDialogs(_ => false);

        await StartPage.NewDatabaseCommand.ExecuteAsync(Shell);

        Assert.Equal(before, Database.DbPath);
        Assert.Empty(StartPage.RecentDatabases);
    }

    // Confirming without having picked a path leaves nothing to create
    [AvaloniaFact]
    public async Task ANewDatabaseWithNowhereToGoIsNotCreated()
    {
        var before = Database.DbPath;
        AnswerDialogs<NewDatabaseDialogViewModel>(dialog => dialog.CustomName = "Discs");

        await StartPage.NewDatabaseCommand.ExecuteAsync(Shell);

        Assert.Equal(before, Database.DbPath);
    }

    [AvaloniaFact]
    public async Task NoDatabaseDialogOpensWithoutAWindowToOpenItOver()
    {
        await StartPage.NewDatabaseCommand.ExecuteAsync(new Border());

        Assert.Empty(Opened);
    }

    #endregion

    #region Opening a database

    [AvaloniaFact]
    public async Task OpeningADatabaseFromTheStartPageLoadsItAndRemembersIt()
    {
        var other = TempPath("vvo");
        var service = new VVO.Core.Services.DatabaseService();
        await service.EnsureDatabaseReadyAsync(other);
        await new VVO.Core.Services.VirtualVolumeService(service)
            .CreateVirtualVolumeAsync("Archive", "CompactDisc");

        PickFiles((_, _) => other);
        using var hidden = new MessageProbe<HideStartPage>();

        await StartPage.OpenDatabaseCommand.ExecuteAsync(Shell);

        Assert.Equal(other, Database.DbPath);
        Assert.Contains(other, Settings.Data.RecentFiles);
        Assert.Single(hidden.All);
    }

    [AvaloniaFact]
    public async Task CancellingTheOpenPickerLeavesTheStartPageWhereItWas()
    {
        var before = Database.DbPath;
        PickFiles((_, _) => null);
        using var hidden = new MessageProbe<HideStartPage>();

        await StartPage.OpenDatabaseCommand.ExecuteAsync(Shell);

        Assert.Equal(before, Database.DbPath);
        Assert.Empty(hidden.All);
    }

    #endregion

    #region Opening the catalogue the application was started on

    /// <summary>A real catalogue somewhere other than the one the test opened for itself.</summary>
    private async Task<string> ACatalogueOnDiskAsync()
    {
        var path = TempPath("vvo");
        var service = new VVO.Core.Services.DatabaseService();
        await service.EnsureDatabaseReadyAsync(path);
        await new VVO.Core.Services.VirtualVolumeService(service)
            .CreateVirtualVolumeAsync("Archive", "CompactDisc");

        return path;
    }

    [AvaloniaFact]
    public async Task ACatalogueNamedOnTheCommandLineIsOpenedAndPutsTheStartPageAway()
    {
        var catalogue = await ACatalogueOnDiskAsync();
        using var hidden = new MessageProbe<HideStartPage>();
        using var ready = new MessageProbe<DatabaseReady>();

        await StartPage.OpenAtStartupAsync(catalogue);

        Assert.Equal(catalogue, Database.DbPath);
        Assert.Single(hidden.All);
        Assert.Single(ready.All);
    }

    // Opened the same way whether it was picked or double-clicked, so it is offered again next time
    [AvaloniaFact]
    public async Task ACatalogueOpenedAtStartupIsRemembered()
    {
        var catalogue = await ACatalogueOnDiskAsync();

        await StartPage.OpenAtStartupAsync(catalogue);

        Assert.Contains(catalogue, Settings.Data.RecentFiles);
        Assert.Equal(catalogue, StartPage.RecentDatabases[0].Path);
    }

    [AvaloniaFact]
    public async Task StartingOnNothingLeavesTheStartPageUp()
    {
        var before = Database.DbPath;
        using var hidden = new MessageProbe<HideStartPage>();

        await StartPage.OpenAtStartupAsync(null);
        await StartPage.OpenAtStartupAsync("   ");

        Assert.Equal(before, Database.DbPath);
        Assert.Empty(hidden.All);
        Assert.Empty(Told);
    }

    // Which is what a shortcut to a catalogue on a drive that is no longer plugged in amounts to
    [AvaloniaFact]
    public async Task ACatalogueThatIsNoLongerThereIsReportedRatherThanOpened()
    {
        var before = Database.DbPath;
        using var hidden = new MessageProbe<HideStartPage>();

        await StartPage.OpenAtStartupAsync(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.vvo"));

        Assert.Equal(before, Database.DbPath);
        Assert.Empty(hidden.All);
        Assert.Equal("Open Database", Assert.Single(Told).Title);
    }

    [AvaloniaFact]
    public async Task AFileThatIsNotACatalogueIsReportedRatherThanOpened()
    {
        var notACatalogue = TempPath("vvo");
        await File.WriteAllTextAsync(notACatalogue, "this is not a database");

        using var hidden = new MessageProbe<HideStartPage>();

        await StartPage.OpenAtStartupAsync(notACatalogue);

        Assert.Empty(hidden.All);
        Assert.Equal("Error", Assert.Single(Told).Title);
    }

    #endregion

    #region The list of recent databases

    [AvaloniaFact]
    public void ForgettingNothingAtAllChangesNothing()
    {
        Settings.AddRecentFile(@"D:\Discs.vvo");
        var startPage = NewStartPage();

        startPage.RemoveRecentCommand.Execute(null);

        Assert.Single(startPage.RecentDatabases);
    }

    [AvaloniaFact]
    public async Task OpeningNothingAtAllChangesNothing()
    {
        var before = Database.DbPath;

        await StartPage.OpenRecentCommand.ExecuteAsync(null);

        Assert.Equal(before, Database.DbPath);
    }

    [AvaloniaFact]
    public void TheListIsShownOnlyOnceThereIsSomethingOnIt()
    {
        Assert.False(StartPage.HaveRecentFiles);

        Settings.AddRecentFile(@"D:\Discs.vvo");

        Assert.True(NewStartPage().HaveRecentFiles);
    }

    [AvaloniaFact]
    public void ARecentDatabaseIsNamedByItsFileRatherThanItsWholePath()
    {
        Settings.AddRecentFile(@"D:\Archive\Discs.vvo");

        Assert.Equal("Discs", NewStartPage().RecentDatabases[0].Name);
    }

    #endregion

    #region The page in either layout

    private Window Shown(bool modern)
    {
        Settings.AddRecentFile(@"D:\Archive\Discs.vvo");
        var window = new Window { Content = new StartPageView { DataContext = NewStartPage() }, Width = 900, Height = 700 };
        window.Show();
        Layout.Apply(modern);
        Pump();

        return window;
    }

    private static T Classed<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.Classes.Contains(name));

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheWaysInNameTheirShortcuts(bool modern)
    {
        var window = Shown(modern);

        var tips = window.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("BigButton"))
            .Select(button => ToolTip.GetTip(button))
            .ToList();

        Assert.Equal(["New Database (Ctrl+N)", "Open Database (Ctrl+O)"], tips);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnlyTheModernPageIsHeadedByTheAppsMarkAndName(bool modern)
    {
        var window = Shown(modern);

        var hero = Classed<StackPanel>(window, "StartHero");
        Assert.Equal(modern, hero.IsEffectivelyVisible);
        Assert.Contains(hero.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Virtual Volume Organizer");
        Assert.Single(hero.GetVisualDescendants().OfType<Image>());
        window.Close();
    }

    // Classic lists the path beside the name, Modern under it
    [AvaloniaTheory]
    [InlineData(true, 1, 0)]
    [InlineData(false, 0, 1)]
    public void ARecentDatabaseShowsItsNameAndPath(bool modern, int pathRow, int pathColumn)
    {
        var window = Shown(modern);

        Assert.Equal("Discs", Classed<TextBlock>(window, "RecentName").Text);
        var path = Classed<TextBlock>(window, "RecentPath");
        Assert.Equal(@"D:\Archive\Discs.vvo", path.Text);
        Assert.True(path.IsEffectivelyVisible);
        Assert.Equal(pathRow, Grid.GetRow(path));
        Assert.Equal(pathColumn, Grid.GetColumn(path));
        window.Close();
    }

    #endregion
}
