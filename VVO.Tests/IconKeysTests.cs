using VVO.Core;
using VVO.Core.Services;
using VVO.UI;

namespace VVO.Tests;

public class IconKeysTests
{
    // Pinned so that changing what is offered is a decision: a removed key leaves catalogues
    // holding an icon nothing draws any more
    [Fact]
    public void TheVolumeIconsAreTheOnesTheGuiHasAlwaysOffered()
    {
        Assert.Equal(
            ["HardDrive", "HardDisk", "DeviceSsdFill", "NvmeFill", "UsbDriveFill", "CompactDisc",
             "FloppyDisk", "MicroSd", "Smartphone", "Archive", "StarFilled"],
            IconKeys.Volumes);
        Assert.Equal("HardDrive", IconKeys.DefaultVolume);
    }

    [Fact]
    public void TheFolderIconsAreTheOnesTheGuiHasAlwaysOffered()
    {
        Assert.Equal(
            ["Folder", "FolderStar", "FolderHeart", "FolderImage", "FolderMusic", "FolderPlay"],
            IconKeys.Folders);
        Assert.Equal("Folder", IconKeys.DefaultFolder);
    }

    [Fact]
    public void TheGuiOffersExactlyWhatCoreLists()
    {
        Assert.Same(IconKeys.Volumes, VirtualVolumeIcons.Keys);
        Assert.Same(IconKeys.Folders, FolderIcons.Keys);
        Assert.Equal(IconKeys.DefaultVolume, VirtualVolumeIcons.Default);
        Assert.Equal(IconKeys.DefaultFolder, FolderIcons.Default);
    }

    // What a catalogue holds has to keep opening after the list moves on
    [Fact]
    public async Task CoreStoresAKeyTheListNoLongerOffers()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.vvo");
        try
        {
            var database = new DatabaseService();
            await database.EnsureDatabaseReadyAsync(path);

            var volume = await new VirtualVolumeService(database).CreateVirtualVolumeAsync("Old", "SomethingRemoved");

            Assert.Equal("SomethingRemoved", volume.Icon);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
