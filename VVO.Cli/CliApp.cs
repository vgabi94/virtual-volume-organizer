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

    public static Option<OutputFormat> FormatOption { get; } = new("--format")
    {
        Description = "json (the default) for programs and agents, or table for people. Errors are JSON either way.",
        DefaultValueFactory = _ => OutputFormat.Json,
        Recursive = true
    };

    // An argument starting with '@' is just text: a search term, a label or a file name. Read as
    // a response file it would fail, or worse, pull in arguments nobody typed
    private static readonly ParserConfiguration Parser = new() { ResponseFileTokenReplacer = null };

    public static RootCommand BuildRoot(IServiceProvider services)
    {
        var root = new RootCommand(
            "Virtual Volume Organizer command line. Reads and edits .vvo catalogues; "
            + "output is JSON on stdout.");

        // Directives such as [suggest] write plain text to stdout, which has to hold JSON alone
        root.Directives.Clear();

        root.Options.Add(QuietOption);
        root.Options.Add(FormatOption);
        root.Subcommands.Add(DbCommands.Create(services));
        root.Subcommands.Add(VolumeCommands.Create(services));
        root.Subcommands.Add(FolderCommands.Create(services));

        foreach (var command in BrowseCommands.Create(services))
        {
            root.Subcommands.Add(command);
        }

        root.Subcommands.Add(SearchCommand.Create(services));
        root.Subcommands.Add(CompareCommand.Create(services));

        foreach (var command in RecordCommands.Create(services))
        {
            root.Subcommands.Add(command);
        }

        root.Subcommands.Add(AboutCommand.Create(services));

        return root;
    }

    public static async Task<int> RunAsync(
        RootCommand root,
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        var parseResult = root.Parse(args, Parser);

        if (parseResult.Errors.Count > 0)
        {
            var message = string.Join(" ", parseResult.Errors.Select(parseError => parseError.Message));
            Json.Write(output, CliException.Usage(message).ToEnvelope());

            // Help for the command that was being typed, kept off stdout so the JSON stays alone there
            var help = root.Parse([.. CommandPath(parseResult), "--help"], Parser);
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
