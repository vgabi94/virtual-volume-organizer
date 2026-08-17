using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VVO.UI.ViewModels;

public record IconChoice(string Key, Geometry? Data, bool IsFlipped);

/// <summary>
/// The icon and colour a sidebar row is drawn with, shared by everything that lets the user
/// choose them. Adding and editing differ only in the header, the button and where the
/// starting values come from.
/// </summary>
public abstract partial class AppearanceDialogViewModel : ObservableObject
{
    public string Header { get; }
    public string ConfirmText { get; }
    public IReadOnlyList<IconChoice> Icons { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    public partial IconChoice? SelectedIcon { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDefaultColor))]
    [NotifyPropertyChangedFor(nameof(SelectedBrush))]
    [NotifyCanExecuteChangedFor(nameof(ResetColorCommand))]
    public partial Color SelectedColor { get; set; }

    public bool IsDefaultColor => SelectedColor == IconColors.Default;

    public IBrush SelectedBrush => new SolidColorBrush(SelectedColor);

    public virtual bool CanConfirm => SelectedIcon != null;

    public string Icon => SelectedIcon?.Key ?? Icons[0].Key;

    /// <summary>
    /// The colour to store: null while the row follows the application default.
    /// </summary>
    public string? ColorHex => IsDefaultColor ? null : SelectedColor.ToString();

    protected AppearanceDialogViewModel(
        string header,
        string confirmText,
        IEnumerable<IconChoice> icons,
        string? icon,
        string? color)
    {
        Header = header;
        ConfirmText = confirmText;
        Icons = icons.ToList();

        SelectedIcon = Icons.FirstOrDefault(choice => choice.Key == icon) ?? Icons[0];
        SelectedColor = !string.IsNullOrWhiteSpace(color) && Color.TryParse(color, out var parsed)
            ? parsed
            : IconColors.Default;
    }

    [RelayCommand(CanExecute = nameof(CanResetColor))]
    private void ResetColor()
    {
        SelectedColor = IconColors.Default;
    }

    private bool CanResetColor() => !IsDefaultColor;
}
