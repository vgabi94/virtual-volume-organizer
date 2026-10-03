using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.CompilerServices;
using System.Text;
using VVO.Cli.Output;

namespace VVO.Cli;

/// <param name="Word">What the user has to type, exactly, to go ahead.</param>
public sealed record ConfirmationRequest(string Title, string ItemName, string Message, string Word);

/// <summary>
/// The terminal's counterpart to the GUI's confirm dialogs: the same warning, and a word the
/// user has to type. There is deliberately no flag to skip it.
/// </summary>
public static class Confirmation
{
    private static readonly ConditionalWeakTable<Symbol, object> PathSymbols = new();

    /// <summary>
    /// Marks an argument or option as naming a file, so the command line handed back to the
    /// user carries it in full and works from whatever directory they paste it into.
    /// </summary>
    public static T TakesPath<T>(this T symbol) where T : Symbol
    {
        PathSymbols.AddOrUpdate(symbol, symbol);
        return symbol;
    }

    /// <summary>
    /// Returns once the user has typed the word. Throws <see cref="CliException"/> when there is
    /// no one at a terminal to ask, or when they answer anything else.
    /// </summary>
    public static void Require(CommandContext context, ConfirmationRequest request)
    {
        var terminal = context.Service<ITerminal>();

        if (!terminal.IsInteractive)
        {
            throw new CliException(
                ExitCode.ConfirmationRequired,
                ErrorCodes.ConfirmationRequired,
                $"{request.Message} Run the command in a terminal to confirm.",
                new Dictionary<string, object?> { ["command"] = CommandLine(context.ParseResult) });
        }

        // Written even under --quiet: a prompt nobody can see would look like a hang
        context.Error.WriteLine(request.Title);
        context.Error.WriteLine(request.ItemName);
        context.Error.WriteLine(request.Message);
        context.Error.Write($"Type {request.Word} to confirm: ");
        context.Error.Flush();

        // Compared exactly, as the GUI does: the typing is the whole point of the confirmation
        if (!string.Equals(terminal.ReadLine(), request.Word, StringComparison.Ordinal))
        {
            throw new CliException(
                ExitCode.Cancelled, ErrorCodes.Cancelled, "Not confirmed. Nothing was changed.");
        }
    }

    /// <summary>
    /// The command as the user would type it to run it themselves, from any directory.
    /// </summary>
    public static string CommandLine(ParseResult parseResult)
    {
        var parts = new List<string> { "vvo" };
        var paths = PathTokens(parseResult);

        foreach (var token in parseResult.Tokens)
        {
            parts.Add(Quote(paths.Contains(token) ? Path.GetFullPath(token.Value) : token.Value));
        }

        return string.Join(' ', parts);
    }

    // The values given to arguments and options marked as paths, on the command and its parents
    private static HashSet<Token> PathTokens(ParseResult parseResult)
    {
        var tokens = new HashSet<Token>(ReferenceEqualityComparer.Instance);

        for (SymbolResult? command = parseResult.CommandResult; command != null; command = command.Parent)
        {
            if (command is not CommandResult commandResult)
                continue;

            foreach (var child in commandResult.Children)
            {
                Symbol symbol = child switch
                {
                    ArgumentResult argument => argument.Argument,
                    OptionResult option => option.Option,
                    _ => commandResult.Command
                };

                if (PathSymbols.TryGetValue(symbol, out _))
                {
                    tokens.UnionWith(child.Tokens);
                }
            }
        }

        return tokens;
    }

    // The Windows command-line convention, which is what vvo.exe parses its arguments with
    private static string Quote(string value)
    {
        if (value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c == '"'))
            return value;

        var quoted = new StringBuilder("\"");
        var backslashes = 0;

        foreach (var c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            // Backslashes count only when they run into a quote
            quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            quoted.Append(c);
            backslashes = 0;
        }

        // ...and into the closing quote
        quoted.Append('\\', backslashes * 2);
        quoted.Append('"');

        return quoted.ToString();
    }
}
