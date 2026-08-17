using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VVO.UI;

/// <summary>
/// The icon a row in the volume explorer is drawn with, chosen from its extension.
/// Mirrors the "File types" section of Icons/IconsResource.axaml.
/// </summary>
public static class FileIcons
{
    private static readonly Dictionary<string, Geometry?> Cache = new();

    // Drawn mirrored by the set they came from, and put back the right way up on display
    private static readonly HashSet<string> FlippedKeys = ["FileEarmarkBinaryFill"];

    public static string KeyFor(string extension, bool isFolder)
    {
        return isFolder ? "FolderSolid" : KeyFor(extension.TrimStart('.').ToLowerInvariant());
    }

    public static bool IsFlipped(string key) => FlippedKeys.Contains(key);

    private static string KeyFor(string extension)
    {
        return extension switch
        {
            "jpg" or "jpeg" or "png" or "gif" or "bmp" or "svg" or "webp" or "ico" => "FileImageSolid",
            "mp3" or "wav" or "flac" or "aac" or "ogg" or "m4a" => "FileAudioSolid",
            "mp4" or "mkv" or "avi" or "mov" or "wmv" or "flv" or "webm" => "FileVideoSolid",
            "zip" or "rar" or "7z" or "tar" or "gz" or "bz2" => "FileZipperSolid",

            "pdf" => "FilePdfSolid",
            "doc" or "docx" or "rtf" => "FileWordSolid",
            "xls" or "xlsx" => "FileExcelSolid",
            "csv" => "FileCsvSolid",
            "ppt" or "pptx" => "FilePowerpointSolid",
            "txt" or "md" or "log" => "FileLinesSolid",

            "cs" or "cpp" or "c" or "h" or "java" or "py" or "js" or "ts" or "html" or "css" => "FileCodeSolid",
            "json" or "xml" or "yaml" or "yml" or "ini" or "config" => "FileCog",
            "exe" or "msi" or "bat" or "cmd" or "sh" or "app" => "FileEarmarkBinaryFill",

            "iso" or "img" or "vhd" or "vmdk" => "CompactDiscSolid",
            "db" or "sqlite" or "mdf" or "ldf" => "DatabaseSolid",

            _ => "FileSolid"
        };
    }

    // Every row asks for one of a handful of icons, so the resource lookup is done once each
    public static Geometry? Lookup(string key)
    {
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var geometry = Application.Current?.TryFindResource(key, out var resource) == true
            ? resource as Geometry
            : null;

        Cache[key] = geometry;
        return geometry;
    }
}
