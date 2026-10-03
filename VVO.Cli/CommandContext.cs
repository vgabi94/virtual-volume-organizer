using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using VVO.Cli.Output;

namespace VVO.Cli;

/// <summary>
/// What a command's action gets to work with.
/// </summary>
public sealed class CommandContext(ParseResult parseResult, IServiceProvider services, CancellationToken cancellationToken)
{
    public ParseResult ParseResult { get; } = parseResult;
    public CancellationToken CancellationToken { get; } = cancellationToken;

    public TextWriter Output => ParseResult.InvocationConfiguration.Output;
    public TextWriter Error => ParseResult.InvocationConfiguration.Error;

    public IProgress<string> Progress => ParseResult.GetValue(CliApp.QuietOption)
        ? SilentProgress.Instance
        : new StderrProgress(Error);

    /// <summary>
    /// Lets a command that succeeded still say something through its exit code, the way
    /// compare --exit-code reports differences.
    /// </summary>
    public ExitCode ExitCode { get; set; } = ExitCode.Success;

    public T Service<T>() where T : notnull => services.GetRequiredService<T>();
}

public static class CommandActions
{
    /// <summary>
    /// Runs the action and writes what it returns, or the error it ended in, as the one JSON
    /// document on stdout.
    /// </summary>
    public static void SetJsonAction(
        this Command command, IServiceProvider services, Func<CommandContext, Task<object?>> action)
    {
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = new CommandContext(parseResult, services, cancellationToken);

            try
            {
                var result = await action(context);
                Json.Write(context.Output, result);
                return (int)context.ExitCode;
            }
            catch (Exception e)
            {
                var error = ErrorMapper.Map(e);
                Json.Write(context.Output, error.ToEnvelope());
                return (int)error.ExitCode;
            }
        });
    }
}
