using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VVO.UI.ViewModels;

// Nothing here is undoable: the dialog only gathers a name and a path for its caller
public partial class NewDatabaseDialogViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    public partial string CustomName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    public partial string DatabasePath { get; set; } = string.Empty;

    public bool CanCreate => !string.IsNullOrWhiteSpace(CustomName) && !string.IsNullOrWhiteSpace(DatabasePath);

    [RelayCommand]
    private async Task SelectPath(Visual visual)
    {
        string defaultFileName = !string.IsNullOrWhiteSpace(CustomName)
            ? (CustomName.EndsWith(".vvo", StringComparison.OrdinalIgnoreCase) ? CustomName : $"{CustomName}.vvo")
            : "database.vvo";

        var localPath = await DatabaseFiles.PickToSaveAsync(
            visual, defaultFileName, "Select Database Save Path");

        if (!string.IsNullOrEmpty(localPath))
        {
            DatabasePath = localPath;

            // The name is only suggested, so one already typed is left as it is
            if (string.IsNullOrWhiteSpace(CustomName))
            {
                CustomName = Path.GetFileNameWithoutExtension(localPath);
            }
        }
    }
}
