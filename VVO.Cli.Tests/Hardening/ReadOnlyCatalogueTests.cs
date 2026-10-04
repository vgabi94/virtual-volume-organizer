namespace VVO.Cli.Tests.Hardening;

// Catalogues of offline media are often kept where they can't be written: a share, a disc
public class ReadOnlyCatalogueTests
{
    [Theory]
    [MemberData(nameof(CommandMatrix.OpeningTheCatalogue), MemberType = typeof(CommandMatrix))]
    public async Task ReadsWorkAndWritesAreRefusedBeforeAnythingIsAsked(MatrixCase command)
    {
        using var fixture = await MatrixFixture.CreateAsync();
        fixture.Catalogue.MakeReadOnly();
        var before = File.ReadAllBytes(fixture.Catalogue.Path);
        var terminal = ScriptedTerminal.Typing(command.ConfirmationWord ?? "");

        var result = await CliRunner.RunAsync(command.Valid(fixture), terminal: terminal);

        if (command.Writes)
        {
            Assert.Equal(1, result.ExitCode);
            Assert.Equal("read_only", result.ErrorCode);
            Assert.Contains("can only be read", result.Error.GetProperty("message").GetString());
            Assert.Equal(0, terminal.Reads);
        }
        else
        {
            Assert.True(result.ExitCode == 0, result.Stdout);
        }

        Assert.Equal(before, File.ReadAllBytes(fixture.Catalogue.Path));
    }

    [Fact]
    public async Task AReadOnlyFileIsNotReplacedByNew()
    {
        using var fixture = await MatrixFixture.CreateAsync();
        fixture.Catalogue.MakeReadOnly();
        var before = File.ReadAllBytes(fixture.Catalogue.Path);

        var result = await CliRunner.RunAsync(
            ["db", "new", fixture.Catalogue.Path], terminal: ScriptedTerminal.Typing("replace"));

        Assert.Equal("read_only", result.ErrorCode);
        Assert.Equal(before, File.ReadAllBytes(fixture.Catalogue.Path));
    }

    [Fact]
    public async Task AReadOnlyCatalogueCanBeCopiedAndTheCopyWritten()
    {
        using var fixture = await MatrixFixture.CreateAsync();
        fixture.Catalogue.MakeReadOnly();
        var copy = fixture.ScratchPath("writable.vvo");

        await CliRunner.RunAsync("db", "copy", copy, "--db", fixture.Catalogue.Path);
        File.SetAttributes(copy, File.GetAttributes(copy) & ~FileAttributes.ReadOnly);
        var result = await CliRunner.RunAsync("volume", "create", "New", "--db", copy);

        Assert.Equal(0, result.ExitCode);
    }
}
