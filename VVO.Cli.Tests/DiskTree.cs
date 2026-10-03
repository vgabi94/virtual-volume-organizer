namespace VVO.Cli.Tests;

/// <summary>
/// Real folders and files in a temp directory, for the commands that scan the disk.
/// </summary>
public sealed class DiskTree : IDisposable
{
    public string Root { get; }

    public DiskTree()
    {
        Root = Path.Combine(Path.GetTempPath(), $"VVO_Disk_{Guid.NewGuid()}");
        Directory.CreateDirectory(Root);
    }

    public string PathOf(string relativePath) => Path.Combine(Root, relativePath);

    public DiskTree Folder(string relativePath)
    {
        Directory.CreateDirectory(PathOf(relativePath));
        return this;
    }

    public DiskTree File(string relativePath, int size = 0, DateTime? modified = null)
    {
        var path = PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, new byte[size]);

        if (modified is { } time)
        {
            System.IO.File.SetLastWriteTimeUtc(path, time);
        }

        return this;
    }

    public DiskTree Hidden(string relativePath)
    {
        var path = PathOf(relativePath);
        System.IO.File.SetAttributes(path, System.IO.File.GetAttributes(path) | FileAttributes.Hidden);
        return this;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch { }
    }
}
