using VVO.Core.Services;

namespace VVO.Cli.Output;

public static class ErrorMapper
{
    public static CliException Map(Exception exception)
    {
        return exception switch
        {
            CliException cli => cli,
            DatabaseBusyException => new CliException(ExitCode.DatabaseBusy, ErrorCodes.DatabaseBusy, exception.Message),
            CatalogueItemNotFoundException notFound => CliException.NotFound(MessageOf(notFound)),
            FileNotFoundException or DirectoryNotFoundException => CliException.NotFound(exception.Message),
            ArgumentException argument => CliException.Usage(MessageOf(argument)),
            InvalidDataException => new CliException(ExitCode.Error, ErrorCodes.InvalidDatabase, exception.Message),
            OperationCanceledException => new CliException(
                ExitCode.Cancelled, ErrorCodes.Cancelled, "The operation was cancelled."),
            _ => new CliException(ExitCode.Error, ErrorCodes.Error, exception.Message)
        };
    }

    // The parameter name is for a developer reading a stack trace, not for the agent
    private static string MessageOf(ArgumentException exception)
    {
        var suffix = $" (Parameter '{exception.ParamName}')";
        return exception.ParamName != null && exception.Message.EndsWith(suffix, StringComparison.Ordinal)
            ? exception.Message[..^suffix.Length]
            : exception.Message;
    }
}
