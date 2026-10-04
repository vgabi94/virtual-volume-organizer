using CommunityToolkit.Mvvm.ComponentModel;

namespace VVO.UI.ViewModels;

public partial class OptionsDialogViewModel : ObservableObject
{
    private readonly Settings _settings;

    [ObservableProperty]
    public partial bool ShowFolderDetailsAlways { get; set; }

    [ObservableProperty]
    public partial int MaxRecentDatabases { get; set; }

    [ObservableProperty]
    public partial bool ScanHiddenAndSystem { get; set; }

    [ObservableProperty]
    public partial bool DarkTheme { get; set; }

    [ObservableProperty]
    public partial bool MicaTheme { get; set; }

    [ObservableProperty]
    public partial bool ModernLayout { get; set; }

    public OptionsDialogViewModel(Settings settings)
    {
        _settings = settings;

        ShowFolderDetailsAlways = settings.Data.ShowFolderDetailsAlways;
        MaxRecentDatabases = settings.Data.MaxRecentFiles;
        ScanHiddenAndSystem = settings.Data.ScanHiddenAndSystem;
        DarkTheme = settings.IsDarkTheme;
        MicaTheme = settings.IsMicaTheme;
        ModernLayout = settings.IsModernLayout;
    }

    /// <summary>
    /// Writes the settings back. The sidebar reads its own copy of the folder-detail flag at
    /// startup, so the caller has to hand that one on rather than let it be picked up later.
    /// </summary>
    public void Apply()
    {
        _settings.SetShowFolderDetailsAlways(ShowFolderDetailsAlways);
        _settings.SetMaxRecentFiles(MaxRecentDatabases);
        _settings.SetScanHiddenAndSystem(ScanHiddenAndSystem);

        // Nothing else owns the theme or the layout, so they are put on here rather than handed to a caller
        _settings.SetDarkTheme(DarkTheme);
        _settings.SetMicaTheme(MicaTheme);
        Theme.Apply(DarkTheme, MicaTheme);

        _settings.SetModernLayout(ModernLayout);
        Layout.Apply(ModernLayout);
    }
}
