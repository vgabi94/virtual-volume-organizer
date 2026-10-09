using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.CompilerServices;
using System.Text;
using VVO.Cli.Output;
using VVO.Core;

namespace VVO.Cli;

/// <param name="Word">What the user has to type, exactly, to go ahead.</param>
/// <param name="Explanation">Shown at the terminal ahead of the warning, such as what a rescan would change.</param>
/// <param name="Facts">Given to an agent with confirmation_required, so it can tell the user what is at stake.</param>
public sealed record ConfirmationRequest(
    string Title,
    string ItemName,
    string Message,
    string Word,
    IReadOnlyList<string>? Explanation = null,
    IReadOnlyDictionary<string, object?>? Facts = null);

/// <summary>
/// Somewhere other than the terminal to ask the user, such as an MCP client's elicitation form.
/// The answer has to come from the user, never from the agent calling the command.
/// </summary>
public interface IConfirmationPrompt
{
    /// <summary>False when the other end has no way to ask the user.</summary>
    bool CanAsk { get; }

    /// <returns>What the user typed, or null when they declined.</returns>
    Task<string?> AskAsync(ConfirmationRequest request, CancellationToken cancellationToken);
}

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
    public static async Task RequireAsync(CommandContext context, ConfirmationRequest request)
    {
        if (context.OptionalService<IConfirmationPrompt>() is { CanAsk: true } prompt)
        {
            Check(await prompt.AskAsync(request, context.CancellationToken), request);
            return;
        }

        var terminal = context.Service<ITerminal>();

        if (!terminal.IsInteractive)
        {
            var details = new Dictionary<string, object?>(request.Facts ?? new Dictionary<string, object?>())
            {
                ["command"] = CommandLine(context.ParseResult)
            };

            throw new CliException(
                ExitCode.ConfirmationRequired,
                ErrorCodes.ConfirmationRequired,
                $"{request.Message} Run the command in a terminal to confirm.",
                details);
        }

        // Written even under --quiet: a prompt nobody can see would look like a hang
        foreach (var line in request.Explanation ?? [])
        {
            context.Error.WriteLine(line);
        }

        context.Error.WriteLine(request.Title);
        context.Error.WriteLine(request.ItemName);
        context.Error.WriteLine(request.Message);
        context.Error.Write($"Type {request.Word} to confirm: ");
        context.Error.Flush();

        Check(terminal.ReadLine(), request);
    }

    private static void Check(string? answer, ConfirmationRequest request)
    {
        // Compared exactly, as the GUI does: the typing is the whole point of the confirmation
        if (!string.Equals(answer, request.Word, StringComparison.Ordinal))
        {
            throw new CliException(
                ExitCode.Cancelled, ErrorCodes.Cancelled, "Not confirmed. Nothing was changed.");
        }
    }

    /// <summary>
    /// Asks about a deletion in the words the GUI's delete dialog uses.
    /// </summary>
    public static Task RequireDeletionAsync(CommandContext context, DeletionWarning warning)
    {
        return RequireAsync(context, new ConfirmationRequest(
            warning.Title, warning.ItemName, warning.Message, DeletionWarnings.ConfirmationWord));
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

        for (var command = parseResult.CommandResult; command != null; command = command.Parent as CommandResult)
        {
            foreach (var child in command.Children)
            {
                Symbol symbol = child switch
                {
                    ArgumentResult argument => argument.Argument,
                    OptionResult option => option.Option,
                    _ => command.Command
                };

                if (PathSymbols.TryGetValue(symbol, out _))
                {
                    tokens.UnionWith(child.Tokens);
                }
            }
        }

        return tokens;
    }

    /// <summary>
    /// Another command for the user or agent to run, written the same way.
    /// </summary>
    public static string CommandLine(params string[] args) =>
        string.Join(' ', args.Prepend("vvo").Select((arg, index) => index == 0 ? arg : Quote(arg)));

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
