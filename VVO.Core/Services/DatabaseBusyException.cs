namespace VVO.Core.Services;

/// <summary>
/// The catalogue stayed in use by another program for as long as it was waited on.
/// </summary>
public class DatabaseBusyException(string path, Exception innerException)
    : IOException(
        $"'{Path.GetFileName(path)}' is in use by another program. Try again once it is done.",
        innerException);
