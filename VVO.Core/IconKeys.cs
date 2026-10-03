namespace VVO.Core;

/// <summary>
/// The icons the GUI and the CLI offer, by key. Core itself stores whatever key it is given: a
/// catalogue can hold one that a later version no longer offers, and still has to open, so these
/// are what to offer rather than what to accept.
/// </summary>
public static class IconKeys
{
    public const string DefaultVolume = "HardDrive";

    /// <summary>
    /// Mirrors the "Virtual Volumes types" section of the GUI's Icons/IconsResource.axaml.
    /// </summary>
    public static IReadOnlyList<string> Volumes { get; } =
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

    public const string DefaultFolder = "Folder";

    public static IReadOnlyList<string> Folders { get; } =
    [
        "Folder",
        "FolderStar",
        "FolderHeart",
        "FolderImage",
        "FolderMusic",
        "FolderPlay"
    ];
}
