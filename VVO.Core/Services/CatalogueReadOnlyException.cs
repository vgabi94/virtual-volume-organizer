namespace VVO.Core.Services;

/// <summary>
/// A write to a catalogue that can only be read: the file is marked read-only, or it sits in a
/// folder, on a share or on media that cannot be written.
/// </summary>
public class CatalogueReadOnlyException(string path, Exception? innerException = null)
    : IOException(
        $"'{Path.GetFileName(path)}' can only be read: it, or the place it is kept, cannot be written.",
        innerException);
