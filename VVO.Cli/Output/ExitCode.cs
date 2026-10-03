namespace VVO.Cli.Output;

/// <summary>
/// Part of the contract with agents: each code means one thing and keeps meaning it.
/// </summary>
public enum ExitCode
{
    Success = 0,
    Error = 1,

    /// <summary>
    /// A comparison asked for --exit-code found differences: the git diff --exit-code convention.
    /// It shares its code with <see cref="Error"/>, as git's does; stdout tells them apart, holding
    /// the comparison rather than an error.
    /// </summary>
    Differences = 1,

    Usage = 2,
    NotFound = 3,
    ConfirmationRequired = 4,
    DatabaseBusy = 5,
    Cancelled = 6
}

public static class ErrorCodes
{
    public const string Error = "error";
    public const string InvalidDatabase = "invalid_database";
    public const string InvalidFile = "invalid_file";
    public const string ReadOnly = "read_only";
    public const string Usage = "usage";
    public const string NotFound = "not_found";
    public const string ConfirmationRequired = "confirmation_required";
    public const string DatabaseBusy = "database_busy";
    public const string Cancelled = "cancelled";
}
