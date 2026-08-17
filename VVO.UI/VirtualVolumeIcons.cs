using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VVO.UI;

/// <summary>
/// The icons a virtual volume can be listed under, by resource key. Mirrors the
/// "Virtual Volumes types" section of Icons/IconsResource.axaml.
/// </summary>
public static class VirtualVolumeIcons
{
    public const string Default = "HardDrive";

    public static IReadOnlyList<string> Keys { get; } =
    [
        "HardDrive",
        "HardDisk",
        "DeviceSsdFill",
        "NvmeFill",
        "UsbDriveFill",
        "CompactDisc",
        "FloppyDisk",
        "MicroSd",
        "Smartphone",
        "Archive",
        "StarFilled"
    ];

    // Icons whose path data is drawn bottom-up, which renders them upside down until flipped
    private static readonly HashSet<string> FlippedKeys = ["Archive", "HardDrive", "UsbDriveFill"];

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
