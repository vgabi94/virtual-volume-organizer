using VVO.Cli;

namespace VVO.Cli.Tests;

/// <summary>
/// Plays the user at the keyboard, answering with the lines it was given and then end of input.
/// </summary>
public sealed class ScriptedTerminal : ITerminal
{
    private readonly Queue<string> _lines;

    private ScriptedTerminal(bool isInteractive, IEnumerable<string> lines)
    {
        IsInteractive = isInteractive;
        _lines = new Queue<string>(lines);
    }

    public static ScriptedTerminal Typing(params string[] lines) => new(true, lines);

    public static ScriptedTerminal Redirected() => new(false, []);

    public bool IsInteractive { get; }

    public int Reads { get; private set; }

    public string? ReadLine()
    {
        Reads++;
        return _lines.TryDequeue(out var line) ? line : null;
    }
}
