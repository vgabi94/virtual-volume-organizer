using Avalonia.Headless.XUnit;
using VVO.Core.Models;
using VVO.UI.ViewModels;

namespace VVO.UiTests;

/// <summary>
/// The commands that act on the database file itself, each of which goes through a file picker
/// there is nobody to click through here.
/// </summary>
public class DatabaseCommandTests : UiTestBase
{
    private SidebarViewModel Sidebar { get; }

    public DatabaseCommandTests()
    {
        Sidebar = NewSidebar();
    }

    private async Task GivenACataloguedDatabaseAsync()
    {
        await Database.InsertItemsAsync<DatabaseMetadata>(
            [new DatabaseMetadata { Id = Guid.NewGuid(), Name = "Discs", Path = Database.DbPath }]);

        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        await AddFolderAsync(volume.Id, "Code");
        await Sidebar.LoadAsync();
    }

    #region Save a copy

    [AvaloniaFact]
    public async Task SavingACopyIsOnlyOfferedOnceADatabaseIsOpen()
    {
        Assert.False(Sidebar.SaveCopyCommand.CanExecute(Shell));

        await GivenACataloguedDatabaseAsync();

        Assert.True(Sidebar.SaveCopyCommand.CanExecute(Shell));
    }

    [AvaloniaFact]
    public async Task SavingACopyWritesTheWholeDatabaseSomewhereElse()
    {
        await GivenACataloguedDatabaseAsync();

        string? target = null;
        PickFiles((_, extension) => target = TempPath(extension));

        await Sidebar.SaveCopyCommand.ExecuteAsync(Shell);

        Assert.NotNull(target);
        Assert.True(File.Exists(target));

        // The copy is a database in its own right, holding what the original held
        var copy = new VVO.Core.Services.DatabaseService();
        await copy.EnsureDatabaseReadyAsync(target!);
        Assert.Equal("test", (await new VVO.Core.Services.VirtualVolumeService(copy)
            .GetVirtualVolumesAsync()).Single().Name);
    }

    [AvaloniaFact]
    public async Task CancellingTheSaveCopyPickerWritesNothing()
    {
        await GivenACataloguedDatabaseAsync();
        PickFiles((_, _) => null);

        await Sidebar.SaveCopyCommand.ExecuteAsync(Shell);

        Assert.Empty(Told);
    }

    #endregion

    #region Export and import

    [AvaloniaFact]
    public async Task ExportingIsOnlyOfferedOnceADatabaseIsOpen()
    {
        Assert.False(Sidebar.ExportCommand.CanExecute(Shell));

        await GivenACataloguedDatabaseAsync();

        Assert.True(Sidebar.ExportCommand.CanExecute(Shell));
    }

    [AvaloniaFact]
    public async Task ExportingWritesTheCatalogueAsJson()
    {
        await GivenACataloguedDatabaseAsync();

        string? json = null;
        PickFiles((_, extension) => json = TempPath(extension));

        await Sidebar.ExportCommand.ExecuteAsync(Shell);

        Assert.NotNull(json);
        var written = await File.ReadAllTextAsync(json!);
        Assert.Contains("\"virtualVolumes\"", written);
        Assert.Contains("test", written);
    }

    [AvaloniaFact]
    public async Task ExportingThenImportingCarriesTheCatalogueIntoAFreshDatabase()
    {
        await GivenACataloguedDatabaseAsync();

        var json = TempPath("json");
        var target = TempPath("vvo");

        PickFiles((_, extension) => extension == "json" ? json : target);

        await Sidebar.ExportCommand.ExecuteAsync(Shell);
        await Sidebar.ImportCommand.ExecuteAsync(Shell);

        Assert.Equal(target, Database.DbPath);
        Assert.Equal("test", (await Volumes.GetVirtualVolumesAsync()).Single().Name);

        // Queues behind the load the import broadcast, which is answered without being waited on
        await Sidebar.LoadAsync();
        Assert.Equal("test", Sidebar.VirtualVolumes.Single().Name);
        Assert.Equal("Code", Sidebar.VirtualVolumes.Single().Folders.Single().Title);
    }

    [AvaloniaFact]
    public async Task AnImportedDatabaseIsRemembered()
    {
        await GivenACataloguedDatabaseAsync();

        var json = TempPath("json");
        var target = TempPath("vvo");
        PickFiles((_, extension) => extension == "json" ? json : target);

        await Sidebar.ExportCommand.ExecuteAsync(Shell);
        await Sidebar.ImportCommand.ExecuteAsync(Shell);

        Assert.Contains(target, Settings.Data.RecentFiles);
    }

    [AvaloniaFact]
    public async Task CancellingTheImportPickerChangesNothing()
    {
        await GivenACataloguedDatabaseAsync();
        var before = Database.DbPath;
        PickFiles((_, _) => null);

        await Sidebar.ImportCommand.ExecuteAsync(Shell);

        Assert.Equal(before, Database.DbPath);
    }

    [AvaloniaFact]
    public async Task ImportingSomethingThatIsNotAnExportReportsItRatherThanThrowing()
    {
        await GivenACataloguedDatabaseAsync();

        var json = TempPath("json");
        await File.WriteAllTextAsync(json, "this is not json");
        PickFiles((_, extension) => extension == "json" ? json : TempPath("vvo"));

        await Sidebar.ImportCommand.ExecuteAsync(Shell);

        Assert.Equal("Error", Assert.Single(Told).Title);
    }

    #endregion

    #region Opening

    [AvaloniaFact]
    public async Task OpeningADatabaseLoadsItAndRemembersIt()
    {
        // A database of its own, catalogued, to be opened by the command
        var other = TempPath("vvo");
        var service = new VVO.Core.Services.DatabaseService();
        await service.EnsureDatabaseReadyAsync(other);
        await service.InsertItemsAsync<DatabaseMetadata>(
            [new DatabaseMetadata { Id = Guid.NewGuid(), Name = "Elsewhere", Path = other }]);
        await new VVO.Core.Services.VirtualVolumeService(service)
            .CreateVirtualVolumeAsync("Archive", "CompactDisc");

        PickFiles((_, _) => other);

        await Sidebar.OpenDatabaseCommand.ExecuteAsync(Shell);

        Assert.Equal(other, Database.DbPath);
        Assert.Contains(other, Settings.Data.RecentFiles);

        // Queues behind the load the open broadcast, which is answered without being waited on
        await Sidebar.LoadAsync();
        Assert.Equal("Elsewhere", Sidebar.DatabaseName);
        Assert.Equal("Archive", Sidebar.VirtualVolumes.Single().Name);
    }

    [AvaloniaFact]
    public async Task CancellingTheOpenPickerLeavesTheDatabaseAlone()
    {
        await GivenACataloguedDatabaseAsync();
        var before = Database.DbPath;
        PickFiles((_, _) => null);

        await Sidebar.OpenDatabaseCommand.ExecuteAsync(Shell);

        Assert.Equal(before, Database.DbPath);
    }

    #endregion

    #region Creating

    [AvaloniaFact]
    public async Task CreatingADatabaseCataloguesItAndAsksForAFirstVolume()
    {
        var target = TempPath("vvo");

        // The new-database dialog, then the first virtual volume it asks for
        AnswerDialogs(dialog =>
        {
            switch (dialog.DataContext)
            {
                case NewDatabaseDialogViewModel newDatabase:
                    newDatabase.CustomName = "Discs";
                    newDatabase.DatabasePath = target;
                    return true;

                case VirtualVolumeDialogViewModel volume:
                    volume.VolumeName = "test";
                    return true;

                default:
                    return false;
            }
        });

        await Sidebar.NewDatabaseCommand.ExecuteAsync(Shell);

        Assert.Equal(target, Database.DbPath);
        Assert.Equal("Discs", (await Database.ReadItemsAsync<DatabaseMetadata>()).Single().Name);
        Assert.Equal("test", (await Volumes.GetVirtualVolumesAsync()).Single().Name);
        Assert.Contains(target, Settings.Data.RecentFiles);
    }

    [AvaloniaFact]
    public async Task CancellingTheNewDatabaseDialogCreatesNothing()
    {
        var before = Database.DbPath;
        AnswerDialogs(_ => false);

        await Sidebar.NewDatabaseCommand.ExecuteAsync(Shell);

        Assert.Equal(before, Database.DbPath);
    }

    #endregion

    #region The start page

    [AvaloniaFact]
    public void TheStartPageRemembersNothingToBeginWith()
    {
        var startPage = NewStartPage();

        Assert.False(startPage.HaveRecentFiles);
        Assert.Empty(startPage.RecentDatabases);
    }

    [AvaloniaFact]
    public void ARememberedDatabaseThatIsGoneIsMarkedAsMissing()
    {
        Settings.AddRecentFile(@"D:\nowhere\gone.vvo");

        var recent = Assert.Single(NewStartPage().RecentDatabases);

        Assert.True(recent.IsMissing);
        Assert.Equal("gone", recent.Name);
        Assert.Contains("no longer there", recent.Tooltip);
    }

    [AvaloniaFact]
    public async Task ARememberedDatabaseThatIsThereIsNotMarked()
    {
        await GivenACataloguedDatabaseAsync();
        Settings.AddRecentFile(Database.DbPath);

        var recent = Assert.Single(NewStartPage().RecentDatabases);

        Assert.False(recent.IsMissing);
        Assert.Equal(Database.DbPath, recent.Tooltip);
    }

    [AvaloniaFact]
    public void ForgettingARecentDatabaseTakesItOffTheList()
    {
        Settings.AddRecentFile(@"D:\nowhere\gone.vvo");
        var startPage = NewStartPage();

        startPage.RemoveRecentCommand.Execute(startPage.RecentDatabases[0]);

        Assert.Empty(startPage.RecentDatabases);
        Assert.Empty(Settings.Data.RecentFiles);
    }

    [AvaloniaFact]
    public async Task OpeningARecentDatabaseThatHasGoneSaysSoAndForgetsIt()
    {
        Settings.AddRecentFile(@"D:\nowhere\gone.vvo");
        var startPage = NewStartPage();

        await startPage.OpenRecentCommand.ExecuteAsync(startPage.RecentDatabases[0]);

        Assert.Equal("Open Database", Assert.Single(Told).Title);
        Assert.Empty(startPage.RecentDatabases);
    }

    [AvaloniaFact]
    public async Task OpeningARecentDatabaseLoadsIt()
    {
        await GivenACataloguedDatabaseAsync();
        var path = Database.DbPath;
        Settings.AddRecentFile(path);

        var startPage = NewStartPage();
        await startPage.OpenRecentCommand.ExecuteAsync(startPage.RecentDatabases[0]);

        Assert.Equal(path, Database.DbPath);
        Assert.Empty(Told);
    }

    #endregion
}
