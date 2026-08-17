using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using VVO.Core.Models;

namespace VVO.UI.ViewModels;

public partial class VirtualVolumeDialogViewModel : AppearanceDialogViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    public partial string VolumeName { get; set; }

    public override bool CanConfirm => base.CanConfirm && !string.IsNullOrWhiteSpace(VolumeName);

    private VirtualVolumeDialogViewModel(
        string header, string confirmText, string volumeName, string? icon, string? color)
        : base(header, confirmText, Choices(), icon, color)
    {
        VolumeName = volumeName;
    }

    public static VirtualVolumeDialogViewModel ForNewVolume()
    {
        return new VirtualVolumeDialogViewModel(
            "New Virtual Volume", "Create", string.Empty, VirtualVolumeIcons.Default, null);
    }

    public static VirtualVolumeDialogViewModel ForExistingVolume(VirtualVolumeRecord record)
    {
        return new VirtualVolumeDialogViewModel(
            "Edit Virtual Volume", "Save", record.Name, record.Icon, record.Color);
    }

    private static IEnumerable<IconChoice> Choices()
    {
        return VirtualVolumeIcons.Keys.Select(key =>
            new IconChoice(key, VirtualVolumeIcons.Lookup(key), VirtualVolumeIcons.IsFlipped(key)));
    }
}
