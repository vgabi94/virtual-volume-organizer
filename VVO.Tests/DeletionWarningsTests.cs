using VVO.Core;

namespace VVO.Tests;

// The GUI's dialog and the CLI's prompt both read these, so the wording is pinned here once
public class DeletionWarningsTests
{
    [Fact]
    public void AFolderGoesWithEverythingUnderIt()
    {
        var warning = DeletionWarnings.ForFolder("photos");

        Assert.Equal("Delete Folder", warning.Title);
        Assert.Equal("photos", warning.ItemName);
        Assert.Equal(
            "Deleting this folder removes it from its virtual volume along with every catalogued file "
            + "and folder under it. This cannot be undone.",
            warning.Message);
    }

    [Fact]
    public void ACatalogueFileLeavesTheDiskAlone()
    {
        var warning = DeletionWarnings.ForCatalogueFile("a.jpg");

        Assert.Equal("Delete File", warning.Title);
        Assert.Equal("a.jpg", warning.ItemName);
        Assert.Equal(
            "Deleting this file removes it from the catalogue. The file on disk is left alone. "
            + "This cannot be undone.",
            warning.Message);
    }

    [Fact]
    public void ACatalogueFolderTakesWhatIsUnderIt()
    {
        var warning = DeletionWarnings.ForCatalogueFolder("2024");

        Assert.Equal("Delete Folder", warning.Title);
        Assert.Equal(
            "Deleting this folder removes it from the catalogue along with every catalogued file "
            + "and folder under it. The files on disk are left alone. This cannot be undone.",
            warning.Message);
    }

    [Theory]
    [InlineData(false, "Deleting these items removes them from the catalogue. The files on disk are left alone. This cannot be undone.")]
    [InlineData(true, "Deleting these items removes them from the catalogue. The files on disk are left alone. Folders take every catalogued file and folder under them. This cannot be undone.")]
    public void SeveralItemsSayWhetherFoldersAreAmongThem(bool includesFolder, string message)
    {
        var warning = DeletionWarnings.ForCatalogueItems(3, includesFolder);

        Assert.Equal("Delete Items", warning.Title);
        Assert.Equal("3 items", warning.ItemName);
        Assert.Equal(message, warning.Message);
    }

    [Theory]
    [InlineData(0, "Deleting this virtual volume cannot be undone. It holds no folders.")]
    [InlineData(1, "Deleting this virtual volume cannot be undone. The 1 folder it holds is deleted with it.")]
    [InlineData(4, "Deleting this virtual volume cannot be undone. The 4 folders it holds are deleted with it.")]
    public void AVirtualVolumeCountsTheFoldersGoingWithIt(int folderCount, string message)
    {
        var warning = DeletionWarnings.ForVirtualVolume("Backups", folderCount);

        Assert.Equal("Delete Virtual Volume", warning.Title);
        Assert.Equal("Backups", warning.ItemName);
        Assert.Equal(message, warning.Message);
    }

    [Fact]
    public void ItemsInASharedScanSayWhereElseTheyGo()
    {
        Assert.Contains("It goes from the other folder listing the same scan too.",
            DeletionWarnings.ForCatalogueFile("a.jpg", otherFolders: 1).Message);
        Assert.Contains("It goes from the 2 other folders listing the same scan too.",
            DeletionWarnings.ForCatalogueFolder("2024", otherFolders: 2).Message);
        Assert.Contains("They go from the 3 other folders listing the same scan too.",
            DeletionWarnings.ForCatalogueItems(4, includesFolder: false, otherFolders: 3).Message);
        Assert.EndsWith("This cannot be undone.", DeletionWarnings.ForCatalogueItems(4, true, 3).Message);
    }

    [Fact]
    public void TheWordIsTheOneTheGuiAsksFor()
    {
        Assert.Equal("delete", DeletionWarnings.ConfirmationWord);
        Assert.Equal(DeletionWarnings.ConfirmationWord, UI.ViewModels.ConfirmDeleteDialogViewModel.RequiredPhrase);
    }
}
