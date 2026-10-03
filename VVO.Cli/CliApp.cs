using System.CommandLine;
using System.CommandLine.Parsing;
using VVO.Cli.Commands;
using VVO.Cli.Output;

namespace VVO.Cli;

/// <summary>
/// The whole command tree, built in one place so the tests can run it in-process.
/// </summary>
public static class CliApp
{
    public static Option<bool> QuietOption { get; } = new("--quiet", "-q")
    {
        Description = "Leave out progress messages on stderr.",
        Recursive = true
    };

    public static RootCommand BuildRoot(IServiceProvider services)
    {
        var root = new RootCommand(
            "Virtual Volume Organizer command line. Reads and edits .vvo catalogues; "
            + "output is JSON on stdout.");

        root.Options.Add(QuietOption);
        root.Subcommands.Add(DbCommands.Create(services));
        root.Subcommands.Add(VolumeCommands.Create(services));
        root.Subcommands.Add(FolderCommands.Create(services));

        return root;
    }

    public static async Task<int> RunAsync(
        RootCommand root,
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        var parseResult = root.Parse(args);

        if (parseResult.Errors.Count > 0)
        {
            var message = string.Join(" ", parseResult.Errors.Select(parseError => parseError.Message));
            Json.Write(output, CliException.Usage(message).ToEnvelope());

            // Help for the command that was being typed, kept off stdout so the JSON stays alone there
            var help = root.Parse([.. CommandPath(parseResult), "--help"]);
            await help.InvokeAsync(new InvocationConfiguration { Output = error, Error = error });

            return (int)ExitCode.Usage;
        }

        return await parseResult.InvokeAsync(
            new InvocationConfiguration { Output = output, Error = error },
            cancellationToken);
    }

    private static IEnumerable<string> CommandPath(ParseResult parseResult)
    {
        var names = new Stack<string>();
        for (var result = parseResult.CommandResult; result.Parent is CommandResult parent; result = parent)
        {
            names.Push(result.Command.Name);
        }

        return names;
    }
}
