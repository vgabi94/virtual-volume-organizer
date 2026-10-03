using System.CommandLine;
using VVO.Cli;

namespace VVO.Cli.Tests.Hardening;

/// <param name="Path">The command as typed, e.g. "folder rescan".</param>
/// <param name="Valid">Arguments that make the command succeed against a fresh fixture, --db included.</param>
/// <param name="Destructive">Asks the user before it changes anything, even with valid arguments.</param>
/// <param name="ConfirmationWord">What the user types to go ahead, for a destructive command.</param>
public sealed record MatrixCase(
    string Path,
    Func<MatrixFixture, string[]> Valid,
    bool Destructive = false,
    string? ConfirmationWord = null)
{
    public override string ToString() => Path;
}

/// <summary>
/// Every command, with a way to run it successfully. The guarantees that hold for all commands
/// are tested by running each of these, so a new command is covered by adding it here, and
/// <see cref="CommandMatrixTests"/> fails until it is.
/// </summary>
public static class CommandMatrix
{
    public static IReadOnlyList<MatrixCase> Cases { get; } =
    [
        new("db new", f => ["db", "new", f.ScratchPath("new.vvo")]),
        new("db info", f => ["db", "info", "--db", f.Catalogue.Path]),
        new("db copy", f => ["db", "copy", f.ScratchPath("copy.vvo"), "--db", f.Catalogue.Path]),
        new("db shrink", f => ["db", "shrink", "--db", f.Catalogue.Path]),
        new("db export", f => ["db", "export", f.ScratchPath("out.json"), "--db", f.Catalogue.Path]),
        new("db import", f => ["db", "import", f.ExportPath, "--db", f.ScratchPath("imported.vvo")]),

        new("volume list", f => ["volume", "list", "--db", f.Catalogue.Path]),
        new("volume create", f => ["volume", "create", "New", "--db", f.Catalogue.Path]),
        new("volume update", f => ["volume", "update", f.Archive.Id.ToString(), "--name", "Old", "--db", f.Catalogue.Path]),
        new("volume delete", f => ["volume", "delete", f.Archive.Id.ToString(), "--db", f.Catalogue.Path],
            Destructive: true, ConfirmationWord: "delete"),

        new("folder list", f => ["folder", "list", "--db", f.Catalogue.Path]),
        new("folder scan", f => ["folder", "scan", f.Disk.Root, "--volume", f.Archive.Id.ToString(), "--db", f.Catalogue.Path]),
        new("folder update", f => ["folder", "update", f.Folder.Id.ToString(), "--label", "Docs", "--db", f.Catalogue.Path]),
        new("folder copy", f => ["folder", "copy", f.Folder.Id.ToString(), "--to", f.Archive.Id.ToString(), "--db", f.Catalogue.Path]),
        new("folder move", f => ["folder", "move", f.Folder.Id.ToString(), "--to", f.Archive.Id.ToString(), "--db", f.Catalogue.Path]),
        new("folder delete", f => ["folder", "delete", f.Copy.Id.ToString(), "--db", f.Catalogue.Path],
            Destructive: true, ConfirmationWord: "delete"),
        new("folder rescan", f => ["folder", "rescan", f.Folder.Id.ToString(), "--db", f.Catalogue.Path],
            Destructive: true, ConfirmationWord: "update"),

        new("ls", f => ["ls", f.Folder.Id.ToString(), "--db", f.Catalogue.Path]),
        new("tree", f => ["tree", f.Folder.Id.ToString(), "--db", f.Catalogue.Path]),
        new("stat", f => ["stat", f.File.Id.ToString(), "--db", f.Catalogue.Path]),
        new("search", f => ["search", "beach", "--db", f.Catalogue.Path]),
        new("compare", f => ["compare", f.Folder.Id.ToString(), "--disk", f.Disk.Root, "--db", f.Catalogue.Path]),
        new("add", f => ["add", f.SubFolder.Id.ToString(), f.Disk.PathOf("readme.txt"), "--db", f.Catalogue.Path]),
        new("rm", f => ["rm", f.File.Id.ToString(), "--db", f.Catalogue.Path],
            Destructive: true, ConfirmationWord: "delete"),

        new("about", _ => ["about"])
    ];

    public static IEnumerable<object[]> All => Cases.Select(item => new object[] { item });

    public static IEnumerable<object[]> TakingDb => Cases
        .Where(item => TakesDb(item.Path))
        .Select(item => new object[] { item });

    public static IEnumerable<object[]> DestructiveCases => Cases
        .Where(item => item.Destructive)
        .Select(item => new object[] { item });

    /// <summary>
    /// The leaf commands of the real command tree, as typed.
    /// </summary>
    public static IReadOnlyList<Command> Leaves() => LeavesWithPaths().Select(leaf => leaf.Command).ToList();

    public static IReadOnlyList<(string Path, Command Command)> LeavesWithPaths()
    {
        using var services = ServiceConfiguration.ConfigureServices();
        var leaves = new List<(string, Command)>();

        void Walk(Command command, string path)
        {
            if (command.Subcommands.Count == 0)
            {
                leaves.Add((path, command));
                return;
            }

            foreach (var child in command.Subcommands)
            {
                Walk(child, path.Length == 0 ? child.Name : $"{path} {child.Name}");
            }
        }

        Walk(CliApp.BuildRoot(services), string.Empty);
        return leaves;
    }

    public static Command Find(string path) => LeavesWithPaths().Single(leaf => leaf.Path == path).Command;

    public static bool TakesDb(string path) => Find(path).Options.Any(option => option.Name == "--db");
}
