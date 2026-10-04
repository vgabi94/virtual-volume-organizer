using CommunityToolkit.Mvvm.ComponentModel;
using VVO.Core;

namespace VVO.UI.ViewModels;

public partial class NameDialogViewModel : ObservableObject
{
    public string Header { get; }
    public string ConfirmText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm), nameof(NameProblem))]
    public partial string Name { get; set; }

    public string? NameProblem => CataloguePath.NameProblem(Name);

    public bool CanConfirm => !string.IsNullOrWhiteSpace(Name) && NameProblem == null;

    public NameDialogViewModel(string header, string confirmText, string name = "")
    {
        Header = header;
        ConfirmText = confirmText;
        Name = name;
    }
}
