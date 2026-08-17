using CommunityToolkit.Mvvm.ComponentModel;

namespace VVO.UI.ViewModels;

public partial class NameDialogViewModel : ObservableObject
{
    public string Header { get; }
    public string ConfirmText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    public partial string Name { get; set; }

    public bool CanConfirm => !string.IsNullOrWhiteSpace(Name);

    public NameDialogViewModel(string header, string confirmText, string name = "")
    {
        Header = header;
        ConfirmText = confirmText;
        Name = name;
    }
}
