using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using VVO.Core.Models;
using VVO.UI.ViewModels;
using VVO.UI.Views;

namespace VVO.UiTests;

/// <summary>
/// The buttons along the bottom of every dialog. Each one is wired in markup to a handler in
/// the code-behind, so a rename on either side only shows up when the button is pressed.
/// </summary>
public class DialogViewTests : UiTestBase
{
    private static Button ButtonOf(Window window, bool cancel) =>
        window.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => cancel ? button.IsCancel : button.IsDefault);

    private static void Press(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Shows the dialog, presses one of its two buttons, and hands back the answer.</summary>
    private async Task<bool> AnswerAsync(Window dialog, bool cancel)
    {
        var answering = dialog.ShowDialog<bool>(Shell);
        Pump();

        Press(ButtonOf(dialog, cancel));

        return await answering;
    }

    #region Confirming and cancelling

    [AvaloniaFact]
    public async Task TheNameDialogHandsBackTheNameOnBeingConfirmed()
    {
        var viewModel = new NameDialogViewModel("Name for the duplicate", "Duplicate", "Code");
        var dialog = new NameDialogView { DataContext = viewModel };

        Assert.True(await AnswerAsync(dialog, cancel: false));
        Assert.Equal("Code", viewModel.Name);
    }

    [AvaloniaFact]
    public async Task TheNameDialogIsRefusedOnBeingCancelled()
    {
        var dialog = new NameDialogView
        {
            DataContext = new NameDialogViewModel("Name for the duplicate", "Duplicate", "Code")
        };

        Assert.False(await AnswerAsync(dialog, cancel: true));
    }

    [AvaloniaFact]
    public async Task TheVirtualVolumeDialogIsConfirmedAndCancelled()
    {
        Assert.True(await AnswerAsync(
            new VirtualVolumeDialogView { DataContext = VirtualVolumeDialogViewModel.ForNewVolume() },
            cancel: false));

        Assert.False(await AnswerAsync(
            new VirtualVolumeDialogView { DataContext = VirtualVolumeDialogViewModel.ForNewVolume() },
            cancel: true));
    }

    [AvaloniaFact]
    public async Task TheFolderDialogIsConfirmedAndCancelled()
    {
        Assert.True(await AnswerAsync(new FolderDialogView { DataContext = SomeFolderDialog() }, cancel: false));
        Assert.False(await AnswerAsync(new FolderDialogView { DataContext = SomeFolderDialog() }, cancel: true));
    }

    [AvaloniaFact]
    public async Task TheNewDatabaseDialogIsConfirmedAndCancelled()
    {
        var viewModel = new NewDatabaseDialogViewModel { CustomName = "Discs", DatabasePath = @"D:\Discs.vvo" };

        Assert.True(await AnswerAsync(new NewDatabaseDialogView { DataContext = viewModel }, cancel: false));
        Assert.False(await AnswerAsync(
            new NewDatabaseDialogView { DataContext = new NewDatabaseDialogViewModel() }, cancel: true));
    }

    [AvaloniaFact]
    public async Task TheCompareTargetDialogIsConfirmedAndCancelled()
    {
        Assert.True(await AnswerAsync(
            new CompareTargetDialogView { DataContext = new CompareTargetDialogViewModel([]) }, cancel: false));

        Assert.False(await AnswerAsync(
            new CompareTargetDialogView { DataContext = new CompareTargetDialogViewModel([]) }, cancel: true));
    }

    [AvaloniaFact]
    public async Task TheOptionsDialogIsConfirmedAndCancelled()
    {
        Assert.True(await AnswerAsync(
            new OptionsDialogView { DataContext = new OptionsDialogViewModel(Settings) }, cancel: false));

        Assert.False(await AnswerAsync(
            new OptionsDialogView { DataContext = new OptionsDialogViewModel(Settings) }, cancel: true));
    }

    // The confirming button here says Delete rather than OK, and is the one a mistyped
    // confirmation leaves disabled
    [AvaloniaFact]
    public async Task TheDeleteConfirmationIsConfirmedAndCancelled()
    {
        var viewModel = ConfirmDeleteDialogViewModel.ForFolder("Code");
        viewModel.Confirmation = "delete";

        Assert.True(await AnswerAsync(new ConfirmDeleteDialogView { DataContext = viewModel }, cancel: false));

        Assert.False(await AnswerAsync(
            new ConfirmDeleteDialogView { DataContext = ConfirmDeleteDialogViewModel.ForFolder("Code") },
            cancel: true));
    }

    #endregion

    #region The dialogs that only close

    [AvaloniaFact]
    public void TheAboutDialogClosesOnItsOwnButton()
    {
        var dialog = new AboutDialogView { DataContext = new AboutDialogViewModel() };
        dialog.Show();
        Pump();

        // The notices link is a HyperlinkButton, so the close button is no longer the only one
        Press(ButtonOf(dialog, cancel: true));

        Assert.False(dialog.IsVisible);
    }

    [AvaloniaFact]
    public void TheDocumentDialogClosesOnItsOwnButton()
    {
        var dialog = new DocumentDialogView { DataContext = DocumentDialogViewModel.Notices() };
        dialog.Show();
        Pump();

        // The scroll viewer around the document brings buttons of its own along
        Press(ButtonOf(dialog, cancel: true));

        Assert.False(dialog.IsVisible);
    }

    [AvaloniaFact]
    public void TheShortcutsDialogClosesOnItsOwnButton()
    {
        var dialog = new ShortcutsDialogView { DataContext = new ShortcutsDialogViewModel() };
        dialog.Show();
        Pump();

        Press(dialog.GetVisualDescendants().OfType<Button>().Single());

        Assert.False(dialog.IsVisible);
    }

    // The SDK appends the source revision to the informational version after a '+', which is
    // noise on screen. This assembly is built with one so the trimming is exercised.
    [AvaloniaFact]
    public void TheAboutDialogNamesTheAppAndAVersionWithNoBuildStampOnIt()
    {
        var viewModel = new AboutDialogViewModel();

        Assert.Equal("Virtual Volume Organizer", viewModel.AppName);
        Assert.Equal("1.4.2", viewModel.Version);
        Assert.Contains("MIT", viewModel.IconsCredit);
    }

    #endregion

    private FolderDialogViewModel SomeFolderDialog()
    {
        var entry = new RootFolderMetadata
        {
            Id = Guid.NewGuid(),
            TreeId = Guid.NewGuid(),
            Path = @"D:\Code",
            LastScanned = DateTime.UtcNow
        };

        return new FolderDialogViewModel(entry, "Code");
    }
}
