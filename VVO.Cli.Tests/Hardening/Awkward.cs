using VVO.Core.Models;
using VVO.Tests;

namespace VVO.Cli.Tests.Hardening;

/// <summary>
/// Data shaped the way real disks and old catalogues are, rather than the way tests usually are.
/// </summary>
public static class Awkward
{
    /// <summary>
    /// Names a file system allows that tend to break whatever handles them next: other scripts,
    /// combining marks, quoting and wildcard characters, a leading '@' or '-', and edge spaces.
    /// Windows refuses a trailing dot or space on disk, so those appear in catalogue data only.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "café.txt",
        "café combining.txt",
        "日本語のファイル.txt",
        "emoji 🎉📁.txt",
        "it's.txt",
        "say \"hi\".txt",
        "100% sure.txt",
        "a_b.txt",
        "[brackets].txt",
        "@home.txt",
        "-dash.txt",
        " leading space.txt",
        "Ünïcödé ÀÉÎÕÜ.txt",
        "tab\tinside.txt"
    ];

    // Windows refuses these on disk
    public static IEnumerable<string> DiskNames => Names.Where(name => !name.Contains('\t') && !name.Contains('"'));

    public static FolderTree Tree(IEnumerable<string> names, string root = "awkward")
    {
        var builder = TestTree.Root(root);
        foreach (var name in names)
        {
            builder.File(name, name.Length);
        }

        return builder.Build();
    }

    /// <summary>
    /// A chain of folders <paramref name="depth"/> deep, with one file at the bottom.
    /// </summary>
    public static FolderTree Chain(int depth, string root = "deep")
    {
        var builder = TestTree.Root(root);

        void Level(TreeBuilder folder, int level)
        {
            if (level == depth)
            {
                folder.File("bottom.txt", 1);
                return;
            }

            folder.Folder($"level{level + 1}", inner => Level(inner, level + 1));
        }

        Level(builder, 0);
        return builder.Build();
    }

    public static FolderTree Wide(int children, string root = "wide")
    {
        var builder = TestTree.Root(root);
        for (var i = 0; i < children; i++)
        {
            builder.File($"file{i:00000}.txt", i);
        }

        return builder.Build();
    }

    #region Damaged catalogues, written past the services that would refuse them

    /// <summary>
    /// A folder entry whose scanned tree is no longer in the catalogue.
    /// </summary>
    public static async Task<RootFolderMetadata> EntryWithoutTreeAsync(TempCatalogue catalogue, Guid volumeId)
    {
        var entry = TestTree.Root("lost").Build().Metadata with { VirtualVolumeId = volumeId };
        await catalogue.Database.InsertItemsAsync([entry]);
        return entry;
    }

    /// <summary>
    /// A tree in which one folder's record is gone, leaving what was under it without a parent.
    /// </summary>
    public static async Task<(RootFolderMetadata Entry, FileRecord Orphan)> RecordWithoutParentAsync(
        TempCatalogue catalogue, Guid volumeId)
    {
        var tree = TestTree.Root("broken").Folder("gone", gone => gone.File("orphan.txt", 1)).Build();
        var entry = await catalogue.AddFolderAsync(volumeId, tree);

        await catalogue.Database.RemoveItemsAsync<FileRecord>([tree.Record("gone").Id]);
        return (entry, tree.Record("orphan.txt"));
    }

    /// <summary>
    /// A folder entry left in a virtual volume that no longer exists.
    /// </summary>
    public static async Task<RootFolderMetadata> EntryInMissingVolumeAsync(TempCatalogue catalogue)
    {
        var volume = await catalogue.AddVolumeAsync("Doomed");
        var entry = await catalogue.AddFolderAsync(volume.Id, TestTree.Root("stranded").File("s.txt", 1).Build());

        await catalogue.Database.RemoveItemsAsync<VirtualVolumeRecord>([volume.Id]);
        return entry;
    }

    /// <summary>
    /// A scanned tree no folder entry refers to.
    /// </summary>
    public static async Task<FolderTree> OrphanTreeAsync(TempCatalogue catalogue)
    {
        var tree = TestTree.Root("orphan").File("stray.txt", 1).Build();
        await catalogue.Database.InsertItemsAsync(tree.Records.ToList());
        return tree;
    }

    #endregion
}
