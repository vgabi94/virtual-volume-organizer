using System;
using System.Collections.Generic;
using VVO.Core.Models;

namespace VVO.UI.Messages;

/// <summary>
/// Sent by the Scanner/MainViewModel when a folder has been successfully scanned and placed
/// in a virtual volume.
/// </summary>
public record FolderAddedMessage(RootFolderMetadata Entry, FileRecord RootRecord);

/// <summary>
/// Sent by the SidebarViewModel when the user selects a folder, carrying the id of its scanned
/// tree, the name it is listed under, the virtual volume holding it and the path it was scanned
/// from. An empty root path leaves the explorer listing catalogue paths alone.
/// </summary>
public record FolderSelectedMessage(Guid TreeId, string Name, string VolumeName, string RootPath = "");

public record HideStartPage();

public record DatabaseReady();

public record UpdateStatusMessage(bool IsVisible, string Message = "", bool IsCancellable = false);

/// <summary>
/// Sent when the user presses Cancel on the status bar. Whichever view model is running a
/// long operation listens for it and cancels its own work.
/// </summary>
public record CancelRequestedMessage();

/// <summary>
/// Sent when the user asks for a device folder to be scanned into the given virtual volume.
/// </summary>
public record AddFolderMessage(Avalonia.Visual Visual, Guid VirtualVolumeId);

/// <summary>
/// Sent by Find, and answered by the volume explorer putting the caret in its search box.
/// </summary>
public record FocusFileSearchMessage();

/// <summary>
/// One scanned tree a search covers, under the name the sidebar lists it by, the virtual volume
/// holding it and the path it was scanned from.
/// </summary>
public record SearchScope(Guid TreeId, string Name, string VolumeName, string RootPath = "");

/// <summary>
/// Sent by the sidebar's Search all box. An empty term calls the search off and puts the
/// volume explorer back where it was.
/// </summary>
public record SearchAllMessage(string Term, IReadOnlyList<SearchScope> Scopes);

/// <summary>
/// Sent after catalogued files are removed from a tree, carrying the root as it now stands
/// so every sidebar row sharing that tree can show the new size.
/// </summary>
public record TreeContentsChangedMessage(FileRecord Root);
