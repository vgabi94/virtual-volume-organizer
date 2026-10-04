using System.Diagnostics;

namespace VVO.Cli.Tests.Hardening;

public class FileSystemTests
{
    private static bool MakeJunction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(start)!;
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    // The junction itself is catalogued; what it points at, here the folder being scanned, is not
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AJunctionLoopIsScannedOnce(bool hidden)
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var catalogue = await TempCatalogue.CreateAsync();
        var volume = await catalogue.AddVolumeAsync("Backups");
        using var disk = new DiskTree().File(@"inner\a.txt", 1);

        if (!MakeJunction(disk.PathOf(@"inner\loop"), disk.Root))
            return;

        string[] args = ["folder", "scan", disk.Root, "--volume", volume.Id.ToString(), "--db", catalogue.Path];
        var result = await ProcessRunner.RunAsync(hidden ? [.. args, "--hidden"] : args);

        Assert.True(result.ExitCode == 0, result.Stdout + result.Stderr);
        Assert.Equal(3, result.Json.GetProperty("folder").GetProperty("recordCount").GetInt32());
    }

    #region Targets that can't be written

    [Fact]
    public async Task CopyingOntoAReadOnlyFileNamesItAndLeavesTheCatalogueAlone()
    {
        using var fixture = await MatrixFixture.CreateAsync();
        var target = fixture.ScratchPath("locked.vvo");
        File.WriteAllText(target, "keep");
        File.SetAttributes(target, FileAttributes.ReadOnly);
        var before = await fixture.SnapshotAsync();

        var result = await CliRunner.RunAsync(
            ["db", "copy", target, "--db", fixture.Catalogue.Path], terminal: ScriptedTerminal.Typing("replace"));

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("error", result.ErrorCode);
        Assert.Contains("locked.vvo", result.Error.GetProperty("message").GetString());
        Assert.Equal("keep", File.ReadAllText(target));
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task ExportingOntoAReadOnlyFileNamesIt()
    {
        using var fixture = await MatrixFixture.CreateAsync();
        var target = fixture.ScratchPath("locked.json");
        File.WriteAllText(target, "keep");
        File.SetAttributes(target, FileAttributes.ReadOnly);

        var result = await CliRunner.RunAsync(
            ["db", "export", target, "--db", fixture.Catalogue.Path], terminal: ScriptedTerminal.Typing("replace"));

        Assert.Equal("error", result.ErrorCode);
        Assert.Contains("locked.json", result.Error.GetProperty("message").GetString());
        Assert.Equal("keep", File.ReadAllText(target));
    }

    [Fact]
    public async Task CopyingIntoAFolderThatCannotBeWrittenNamesTheTarget()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var fixture = await MatrixFixture.CreateAsync();
        var folder = new DirectoryInfo(fixture.ScratchPath("closed"));
        folder.Create();

        var denial = new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.CreateFiles,
            System.Security.AccessControl.AccessControlType.Deny);
        var security = folder.GetAccessControl();
        security.AddAccessRule(denial);
        folder.SetAccessControl(security);

        try
        {
            var result = await CliRunner.RunAsync("db", "copy", Path.Combine(folder.FullName, "copy.vvo"), "--db", fixture.Catalogue.Path);

            Assert.Equal("error", result.ErrorCode);
            Assert.Contains("copy.vvo", result.Error.GetProperty("message").GetString());
        }
        finally
        {
            security.RemoveAccessRule(denial);
            folder.SetAccessControl(security);
        }
    }

    #endregion
}
