using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VVO.UI;

/// <summary>
/// The icons a folder can be listed under, by resource key.
/// </summary>
public static class FolderIcons
{
    public static IReadOnlyList<string> Keys { get; } =
    [
        "Folder",
        "FolderStar",
        "FolderHeart",
        "FolderImage",
        "FolderMusic",
        "FolderPlay"
    ];

    public static string Default => Keys[0];

    // Icons whose path data is drawn bottom-up, which renders them upside down until flipped
    private static readonly HashSet<string> FlippedKeys = [];

    public static bool IsFlipped(string? key) => key != null && FlippedKeys.Contains(key);

    public static Geometry? Lookup(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            key = Default;
        }

        return Application.Current?.TryFindResource(key, out var resource) == true
            ? resource as Geometry
            : null;
    }
}
