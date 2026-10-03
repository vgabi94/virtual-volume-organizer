using System.Diagnostics.CodeAnalysis;

namespace VVO.Cli;

/// <summary>
/// Where a person's answer to a confirmation comes from. Prompts are written to the command's
/// stderr; this is only the reading side, so a test can play the person.
/// </summary>
public interface ITerminal
{
    /// <summary>
    /// False when input is redirected, which is how agents run commands. A command asking for
    /// confirmation must not read anything then, or an agent could answer for the user.
    /// </summary>
    bool IsInteractive { get; }

    string? ReadLine();
}

// Only a real process has a console to read; the hardening process tests run it there
[ExcludeFromCodeCoverage]
public sealed class ConsoleTerminal : ITerminal
{
    public bool IsInteractive => !Console.IsInputRedirected;

    public string? ReadLine() => Console.In.ReadLine();
}
