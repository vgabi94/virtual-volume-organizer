using VVO.Core.Models;

namespace VVO.Cli.Tests.Hardening;

// What every command promises when it is given something wrong, checked on every command at once
public class ErrorContractTests
{
    private static string[] WithDb(string[] args, string db)
    {
        var copy = args.ToArray();
        copy[Array.IndexOf(copy, "--db") + 1] = db;
        return copy;
    }

    #region The catalogue

    [Theory]
    [MemberData(nameof(CommandMatrix.OpeningTheCatalogue), MemberType = typeof(CommandMatrix))]
    public async Task AMissingCatalogueIsNotFoundAndIsNotCreated(MatrixCase command)
    {
        using var fixture = await MatrixFixture.CreateAsync();
        var missing = fixture.ScratchPath("missing.vvo");

        var result = await CliRunner.RunAsync(WithDb(command.Valid(fixture), missing));

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("not_found", result.ErrorCode);
        Assert.False(File.Exists(missing));
    }

    [Theory]
    [MemberData(nameof(CommandMatrix.OpeningTheCatalogue), MemberType = typeof(CommandMatrix))]
    public async Task AFolderGivenAsTheCatalogueIsAUsageError(MatrixCase command)
    {
        using var fixture = await MatrixFixture.CreateAsync();

        var result = await CliRunner.RunAsync(WithDb(command.Valid(fixture), fixture.Scratch));

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(CommandMatrix.OpeningTheCatalogue), MemberType = typeof(CommandMatrix))]
    public async Task AFileThatIsNotACatalogueIsLeftAsItWas(MatrixCase command)
    {
        using var fixture = await MatrixFixture.CreateAsync();
        var notes = fixture.ScratchPath("notes.vvo");
        File.WriteAllText(notes, "These are notes, not a catalogue.");

        var result = await CliRunner.RunAsync(WithDb(command.Valid(fixture), notes));

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("invalid_database", result.ErrorCode);
        Assert.Equal("These are notes, not a catalogue.", File.ReadAllText(notes));
    }

    // Core would take an empty file for a catalogue about to be made and fill it in
    [Theory]
    [MemberData(nameof(CommandMatrix.OpeningTheCatalogue), MemberType = typeof(CommandMatrix))]
    public async Task AnEmptyFileIsNotACatalogueAndStaysEmpty(MatrixCase command)
    {
        using var fixture = await MatrixFixture.CreateAsync();
        var empty = fixture.ScratchPath("empty.vvo");
        File.WriteAllBytes(empty, []);

        var result = await CliRunner.RunAsync(WithDb(command.Valid(fixture), empty));

        Assert.Equal("invalid_database", result.ErrorCode);
        Assert.Equal(0, new FileInfo(empty).Length);
    }

    [Fact]
    public async Task AnEmptyFileCanStillBeMadeIntoACatalogue()
    {
        using var fixture = await MatrixFixture.CreateAsync();
        var empty = fixture.ScratchPath("empty.vvo");
        File.WriteAllBytes(empty, []);

        var created = await CliRunner.RunAsync(["db", "new", empty], terminal: ScriptedTerminal.Typing("replace"));
        var info = await CliRunner.RunAsync("db", "info", "--db", empty);

        Assert.Equal(0, created.ExitCode);
        Assert.Equal(0, info.ExitCode);
    }

    // The wait is Core's and is the same for every command, so a reader and a writer stand for all
    [Theory]
    [InlineData("volume list")]
    [InlineData("volume create")]
    public async Task ACatalogueHeldElsewhereIsBusy(string path)
    {
        using var fixture = await MatrixFixture.CreateAsync();
        var command = CommandMatrix.Cases.Single(item => item.Path == path);
        using var hold = new FileStream(fixture.Catalogue.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await CliRunner.RunAsync(command.Valid(fixture));

        Assert.Equal(5, result.ExitCode);
        Assert.Equal("database_busy", result.ErrorCode);
    }

    #endregion

    #region Ids

    // Every id in a valid invocation, each the one to be spoiled in turn
    public static IEnumerable<object[]> IdsInValidInvocations()
    {
        foreach (var command in CommandMatrix.Cases)
        {
            using var fixture = MatrixFixture.CreateAsync().GetAwaiter().GetResult();
            var args = command.Valid(fixture);

            for (var index = 0; index < args.Length; index++)
            {
                if (Guid.TryParse(args[index], out _))
                    yield return [command, index];
            }
        }
    }

    [Theory]
    [MemberData(nameof(IdsInValidInvocations))]
    public async Task AnIdThatIsNotAGuidIsAUsageError(MatrixCase command, int index)
    {
        using var fixture = await MatrixFixture.CreateAsync();
        var args = command.Valid(fixture);
        args[index] = "photos";

        var result = await CliRunner.RunAsync(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(IdsInValidInvocations))]
    public async Task AnUnknownIdIsNotFoundAndChangesNothing(MatrixCase command, int index)
    {
        using var fixture = await MatrixFixture.CreateAsync();
        var args = command.Valid(fixture);
        args[index] = Guid.NewGuid().ToString();
        var before = await fixture.SnapshotAsync();
        var terminal = ScriptedTerminal.Typing(command.ConfirmationWord ?? "");

        var result = await CliRunner.RunAsync(args, terminal: terminal);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("not_found", result.ErrorCode);
        Assert.Equal(0, terminal.Reads);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    // A volume where a folder is wanted, a file where a folder is wanted, and so on: whatever is
    // said, it is said as a usage or not_found error, never as something having gone wrong
    [Theory]
    [MemberData(nameof(IdsInValidInvocations))]
    public async Task AnIdOfTheWrongKindIsNeverAnUnexpectedError(MatrixCase command, int index)
    {
        using var fixture = await MatrixFixture.CreateAsync();
        var original = command.Valid(fixture)[index];

        Guid[] others =
        [
            fixture.Backups.Id, fixture.Folder.Id, fixture.Folder.TreeId, fixture.SubFolder.Id, fixture.File.Id
        ];

        foreach (var other in others.Where(id => id.ToString() != original))
        {
            using var each = await MatrixFixture.CreateAsync();
            var args = command.Valid(each);
            args[index] = Translate(other, fixture, each).ToString();

            var result = await CliRunner.RunAsync(args, terminal: ScriptedTerminal.Redirected());

            Assert.True(
                result.ErrorCode is null or "usage" or "not_found" or "confirmation_required",
                $"{command} with {args[index]} at {index}: {result.Stdout}");
        }
    }

    // The same item in a second fixture, whose ids differ
    private static Guid Translate(Guid id, MatrixFixture from, MatrixFixture to)
    {
        if (id == from.Backups.Id) return to.Backups.Id;
        if (id == from.Folder.Id) return to.Folder.Id;
        if (id == from.Folder.TreeId) return to.Folder.TreeId;
        if (id == from.SubFolder.Id) return to.SubFolder.Id;
        if (id == from.File.Id) return to.File.Id;
        throw new ArgumentException($"'{id}' is not one of the fixture's ids.");
    }

    #endregion
}
