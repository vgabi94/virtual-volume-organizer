namespace VVO.Cli.Output;

/// <summary>
/// A failure as the agent is told about it: an exit code, a stable error code, a message, and
/// whatever else the command has to say about it.
/// </summary>
public class CliException(
    ExitCode exitCode,
    string code,
    string message,
    IReadOnlyDictionary<string, object?>? details = null)
    : Exception(message)
{
    public ExitCode ExitCode { get; } = exitCode;
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, object?> Details { get; } = details ?? new Dictionary<string, object?>();

    public static CliException Usage(string message) => new(ExitCode.Usage, ErrorCodes.Usage, message);

    public static CliException NotFound(string message) => new(ExitCode.NotFound, ErrorCodes.NotFound, message);

    public object ToEnvelope()
    {
        var error = new Dictionary<string, object?>
        {
            ["code"] = Code,
            ["message"] = Message
        };

        foreach (var (key, value) in Details)
        {
            error[key] = value;
        }

        return new { error };
    }
}
