using VVO.Core.Models;

namespace VVO.Cli.Tests.Hardening;

public class CommandMatrixTests
{
    // Every guarantee across commands rests on this: a command missing here is untested by them all
    [Fact]
    public void EveryCommandIsInTheMatrix()
    {
        var leaves = CommandMatrix.LeavesWithPaths().Select(leaf => leaf.Path).Order().ToList();
        var cases = CommandMatrix.Cases.Select(item => item.Path).Order().ToList();

        Assert.Equal(leaves, cases);
    }

    [Theory]
    [MemberData(nameof(CommandMatrix.All), MemberType = typeof(CommandMatrix))]
    public async Task EveryValidInvocationSucceeds(MatrixCase command)
    {
        using var fixture = await MatrixFixture.CreateAsync();
        var terminal = command.ConfirmationWord != null
            ? ScriptedTerminal.Typing(command.ConfirmationWord)
            : ScriptedTerminal.Redirected();

        var result = await CliRunner.RunAsync(command.Valid(fixture), terminal: terminal);

        Assert.True(result.ExitCode == 0, $"{command}: {result.Stdout}");
    }

    [Fact]
    public async Task TheProcessRunnerRunsTheRealExecutable()
    {
        var result = await ProcessRunner.RunAsync(["--help"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage:", result.Stdout);
    }

    [Fact]
    public void AChainIsAsDeepAsAsked()
    {
        var chain = Awkward.Chain(5);

        Assert.Equal(7, chain.Records.Count);
        Assert.Equal(chain.Record("level4").Id, chain.Record("level5").ParentId);
        Assert.Equal(chain.Record("level5").Id, chain.Record("bottom.txt").ParentId);
    }

    [Fact]
    public async Task TheDamagedCataloguesAreDamagedAsDescribed()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var volume = await catalogue.AddVolumeAsync("Backups");

        var lost = await Awkward.EntryWithoutTreeAsync(catalogue, volume.Id);
        var (_, orphan) = await Awkward.RecordWithoutParentAsync(catalogue, volume.Id);
        var stranded = await Awkward.EntryInMissingVolumeAsync(catalogue);
        var stray = await Awkward.OrphanTreeAsync(catalogue);

        var records = await catalogue.Database.ReadItemsAsync<FileRecord>();
        var entries = await catalogue.Database.ReadItemsAsync<RootFolderMetadata>();
        var volumes = await catalogue.Database.ReadItemsAsync<VirtualVolumeRecord>();

        Assert.DoesNotContain(records, record => record.Id == lost.TreeId);
        Assert.DoesNotContain(records, record => record.Id == orphan.ParentId);
        Assert.DoesNotContain(volumes, item => item.Id == stranded.VirtualVolumeId);
        Assert.DoesNotContain(entries, entry => entry.TreeId == stray.Metadata.TreeId);
    }
}
