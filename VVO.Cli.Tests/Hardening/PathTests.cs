using System.CommandLine;
using System.Runtime.InteropServices;
using VVO.Cli;

namespace VVO.Cli.Tests.Hardening;

public class PathTests : IDisposable
{
    // Beside the test run, so relative paths can be written from the current directory
    private readonly string _here = Path.Combine(Environment.CurrentDirectory, $"VVO_Paths_{Guid.NewGuid()}");

    public PathTests()
    {
        Directory.CreateDirectory(_here);
    }

    public void Dispose() => DiskTree.DeleteWritable(_here);

    private async Task<(string Path, Guid VolumeId)> CatalogueInAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "catalogue.vvo");

        Assert.Equal(0, (await CliRunner.RunAsync("db", "new", path)).ExitCode);
        var volume = await CliRunner.RunAsync("volume", "create", "Backups", "--db", path);

        return (path, volume.Json.GetProperty("id").GetGuid());
    }

    // The ways one catalogue can be named, each needing to land on the same file
    public static IEnumerable<object[]> Spellings() =>
    [
        ["relative with ..", (Func<string, string>)(full =>
            Path.Combine(Path.GetRelativePath(Environment.CurrentDirectory, Path.GetDirectoryName(full)!), "..",
                Path.GetFileName(Path.GetDirectoryName(full)!), Path.GetFileName(full)))],
        ["forward slashes", (Func<string, string>)(full => full.Replace('\\', '/'))],
        ["as it is", (Func<string, string>)(full => full)]
    ];

    [Theory]
    [MemberData(nameof(Spellings))]
    public async Task EverySpellingOfTheCatalogueOpensIt(string how, Func<string, string> spell)
    {
        var (path, _) = await CatalogueInAsync(Path.Combine(_here, "plain"));

        var info = await CliRunner.RunAsync("db", "info", "--db", spell(path));

        Assert.True(info.ExitCode == 0, $"{how}: {info.Stdout}");
        Assert.Equal(path, info.Json.GetProperty("path").GetString());
    }

    // What the user pastes into a terminal has to name the same catalogue, from wherever they are
    [Theory]
    [MemberData(nameof(Spellings))]
    public async Task ThePastedCommandNamesTheSameCatalogue(string how, Func<string, string> spell)
    {
        var (path, volumeId) = await CatalogueInAsync(Path.Combine(_here, "my catalogues ünï 日本"));

        var first = await CliRunner.RunAsync("volume", "delete", volumeId.ToString(), "--db", spell(path));
        var command = first.Error.GetProperty("command").GetString()!;

        Assert.Contains(path, command);

        var pasted = await ProcessRunner.RunCommandLineAsync(command["vvo ".Length..]);

        Assert.True(pasted.ExitCode == 4, $"{how}: {pasted.Stdout}{pasted.Stderr}");
        Assert.Equal(command, pasted.Error.GetProperty("command").GetString());
    }

    [Fact]
    public async Task APathLongerThanTheOldLimitWorks()
    {
        var deep = _here;
        while (deep.Length < 300)
        {
            deep = Path.Combine(deep, "a-rather-long-folder-name");
        }

        try
        {
            Directory.CreateDirectory(deep);
        }
        catch (PathTooLongException)
        {
            // Long paths are switched off on this machine; nothing of ours to test
            return;
        }

        var (path, volumeId) = await CatalogueInAsync(deep);

        var list = await CliRunner.RunAsync("volume", "list", "--db", path);
        var delete = await CliRunner.RunAsync("volume", "delete", volumeId.ToString(), "--db", path);

        Assert.Equal(0, list.ExitCode);
        Assert.Contains(path, delete.Error.GetProperty("command").GetString());
    }

    #region How a folder on disk is named

    [Theory]
    [InlineData("trailing separator")]
    [InlineData("forward slashes")]
    [InlineData("through ..")]
    public async Task AFolderOnDiskIsStoredTheSameHoweverItIsWritten(string how)
    {
        var (catalogue, volumeId) = await CatalogueInAsync(Path.Combine(_here, "catalogue"));
        var folder = Path.Combine(_here, "my photos");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(Path.Combine(folder, "a.jpg"), "a");

        var written = how switch
        {
            "trailing separator" => folder + Path.DirectorySeparatorChar,
            "forward slashes" => folder.Replace('\\', '/'),
            _ => Path.Combine(folder, "sub", "..")
        };

        var scan = await CliRunner.RunAsync("folder", "scan", written, "--volume", volumeId.ToString(), "--db", catalogue);
        var stored = scan.Json.GetProperty("folder");

        Assert.Equal("my photos", stored.GetProperty("title").GetString());
        Assert.Equal(folder, stored.GetProperty("path").GetString());
    }

    #endregion

    #region Quoting, checked against Windows' own splitting

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static string[] SplitAsWindowsDoes(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var count);
        try
        {
            return Enumerable.Range(0, count)
                .Select(index => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, index * IntPtr.Size))!)
                .ToArray();
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [Theory]
    [InlineData(@"C:\My Folder\")]
    [InlineData(@"C:\My Folder\\")]
    [InlineData(@"ends in a backslash\")]
    [InlineData("say \"hi\"")]
    [InlineData("\"")]
    [InlineData(@"\""")]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData("tab\tinside")]
    [InlineData("plain")]
    public async Task AnArgumentComesBackAsItWasGiven(string value)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var label = new Option<string>("--label");
        var command = new Command("echo") { label };
        command.SetJsonAction(null!, context =>
            Task.FromResult<object?>(new { Command = Confirmation.CommandLine(context.ParseResult) }));

        var result = await CliRunner.RunAsync(["echo", "--label", value], (root, _) => root.Subcommands.Add(command));
        var line = result.Json.GetProperty("command").GetString()!;

        Assert.Equal(["vvo", "echo", "--label", value], SplitAsWindowsDoes(line));
    }

    #endregion
}
