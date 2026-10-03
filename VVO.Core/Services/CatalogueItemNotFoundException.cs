namespace VVO.Core.Services;

/// <summary>
/// An id that names nothing in the open catalogue. Still an <see cref="ArgumentException"/>, as
/// every bad argument here is, but one a caller can tell apart from input that is merely invalid.
/// </summary>
public class CatalogueItemNotFoundException(string message, string? paramName)
    : ArgumentException(message, paramName);
