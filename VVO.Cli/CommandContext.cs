using System.CommandLine;
using System.Text.Json;
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

    public T? OptionalService<T>() where T : class => services.GetService<T>();
}

public static class CommandActions
{
    /// <summary>
    /// Runs the action and writes what it returns to stdout, as one JSON document or as the table
    /// asked for. An error it ends in is written as JSON whatever the format.
    /// </summary>
    /// <param name="table">How --format table shows the result. A plain summary of it otherwise.</param>
    public static void SetJsonAction(
        this Command command,
        IServiceProvider services,
        Func<CommandContext, Task<object?>> action,
        Func<JsonElement, string>? table = null)
    {
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = new CommandContext(parseResult, services, cancellationToken);

            try
            {
                var result = await action(context);

                if (parseResult.GetValue(CliApp.FormatOption) == OutputFormat.Table)
                {
                    var element = JsonSerializer.SerializeToElement(result, Json.Options);
                    context.Output.WriteLine((table ?? TableFormat.Summary)(element));
                }
                else
                {
                    Json.Write(context.Output, result);
                }

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
