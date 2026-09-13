using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using VVO.UI;

namespace VVO.UiTests;

/// <summary>
/// The pickers a file or a folder is chosen through. Headless supplies a picker that answers
/// every prompt with nothing picked, so the paths a cancelling user takes are the ones that
/// run here of their own accord.
/// </summary>
public class PickerTests : UiTestBase
{
    // Nothing is parented, so there is no window to hang a picker off
    private static Border Unparented => new();

    #region The database file pickers

    [AvaloniaFact]
    public async Task OpeningADatabaseAsksForOneWithTheVvoExtension()
    {
        string? asked = null;
        PickFiles((_, extension) => { asked = extension; return null; });

        Assert.Null(await DatabaseFiles.PickToOpenAsync(Shell));
        Assert.Equal("vvo", asked);
    }

    [AvaloniaFact]
    public async Task SavingADatabasePassesOnTheNameToSuggest()
    {
        string? suggested = null;
        PickFiles((name, _) => { suggested = name; return null; });

        Assert.Null(await DatabaseFiles.PickToSaveAsync(Shell, "Discs copy"));
        Assert.Equal("Discs copy", suggested);
    }

    [AvaloniaFact]
    public async Task TheJsonPickersAskForJson()
    {
        var asked = new List<string>();
        PickFiles((_, extension) => { asked.Add(extension); return null; });

        await DatabaseFiles.PickJsonToOpenAsync(Shell);
        await DatabaseFiles.PickJsonToSaveAsync(Shell, "Discs");

        Assert.Equal(["json", "json"], asked);
    }

    [AvaloniaFact]
    public async Task APickerAnsweredWithNothingPickedReturnsNothing()
    {
        Assert.Null(await DatabaseFiles.PickToOpenAsync(Shell));
        Assert.Null(await DatabaseFiles.PickToSaveAsync(Shell, "Discs"));
        Assert.Null(await DatabaseFiles.PickJsonToOpenAsync(Shell));
        Assert.Null(await DatabaseFiles.PickJsonToSaveAsync(Shell, "Discs"));
    }

    [AvaloniaFact]
    public async Task NoPickerIsShownWithoutAWindowToShowItOver()
    {
        Assert.Null(await DatabaseFiles.PickToOpenAsync(Unparented));
        Assert.Null(await DatabaseFiles.PickToSaveAsync(Unparented, "Discs"));
        Assert.Null(await DatabaseFiles.PickJsonToOpenAsync(Unparented));
        Assert.Null(await DatabaseFiles.PickJsonToSaveAsync(Unparented, "Discs"));
    }

    [AvaloniaFact]
    public void TheDatabaseFileTypeIsDescribedTheSameWayEverywhere()
    {
        Assert.Equal(["*.vvo"], DatabaseFiles.FileType.Patterns);
        Assert.Equal("VVO Database", DatabaseFiles.FileType.Name);
    }

    #endregion

    #region The folder picker

    [AvaloniaFact]
    public async Task ThePickedFolderIsHandedBack()
    {
        PickFolder(@"D:\Code");

        Assert.Equal(@"D:\Code", await FolderPicker.PickAsync(Shell, "Select Folder to Scan"));
    }

    [AvaloniaFact]
    public async Task TheFolderPickerIsToldWhatItIsPickingFor()
    {
        string? title = null;
        FolderPicker.Picking = asked => { title = asked; return null; };

        await FolderPicker.PickAsync(Shell, "Select Folder to Compare Against");

        Assert.Equal("Select Folder to Compare Against", title);
    }

    [AvaloniaFact]
    public async Task AFolderPickerAnsweredWithNothingPickedReturnsNothing()
    {
        Assert.Null(await FolderPicker.PickAsync(Shell, "Select Folder to Scan"));
    }

    [AvaloniaFact]
    public async Task NoFolderPickerIsShownWithoutAWindowToShowItOver()
    {
        Assert.Null(await FolderPicker.PickAsync(Unparented, "Select Folder to Scan"));
    }

    [AvaloniaFact]
    public async Task SeveralFoldersAreHandedBackTogether()
    {
        PickFolders(@"D:\Code", @"D:\Photos");

        Assert.Equal(
            [@"D:\Code", @"D:\Photos"],
            await FolderPicker.PickManyAsync(Shell, "Select Folder(s) to Add"));
    }

    [AvaloniaFact]
    public async Task AMultiFolderPickerAnsweredWithNothingPickedReturnsNothing()
    {
        Assert.Empty(await FolderPicker.PickManyAsync(Shell, "Select Folder(s) to Add"));
    }

    #endregion

    #region The disk file picker

    [AvaloniaFact]
    public async Task SeveralFilesAreHandedBackTogether()
    {
        PickDiskFiles(@"D:\a.txt", @"D:\b.txt");

        Assert.Equal(
            [@"D:\a.txt", @"D:\b.txt"],
            await DiskFiles.PickManyAsync(Shell, "Select File(s) to Add"));
    }

    [AvaloniaFact]
    public async Task ADiskFilePickerAnsweredWithNothingPickedReturnsNothing()
    {
        Assert.Empty(await DiskFiles.PickManyAsync(Shell, "Select File(s) to Add"));
    }

    [AvaloniaFact]
    public async Task NoDiskFilePickerIsShownWithoutAWindowToShowItOver()
    {
        Assert.Empty(await DiskFiles.PickManyAsync(Unparented, "Select File(s) to Add"));
    }

    #endregion
}
