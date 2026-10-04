using System.Runtime.CompilerServices;

namespace VVO.Cli.Tests;

/// <summary>
/// Output held against checked-in files under Contract/. A failure means the contract changed: if
/// that was meant, update the snapshot in the same commit.
/// </summary>
public static class Snapshot
{
    public static void AssertMatches(string actual, string name)
    {
        actual = actual.ReplaceLineEndings("\n").TrimEnd();
        var path = Path.Combine(Folder(), $"{name}");

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual + "\n");
            Assert.Fail($"No snapshot for {name}; wrote one to {path}. Review it and run again.");
        }

        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd(), actual);
    }

    private static string Folder([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Contract");
}
