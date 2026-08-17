using VVO.UI.ViewModels;

namespace VVO.Tests;

/// <summary>
/// The typed confirmation is the only thing standing between the user and a delete that takes
/// the scanned tree with it, so what enables the button is worth pinning down.
/// </summary>
public class ConfirmDeleteDialogViewModelTests
{
    [Fact]
    public void TheDeleteButtonStartsDisabled()
    {
        Assert.False(ConfirmDeleteDialogViewModel.ForFolder("Code").CanDelete);
    }

    [Fact]
    public void TypingThePhraseEnablesTheDeleteButton()
    {
        var viewModel = ConfirmDeleteDialogViewModel.ForFolder("Code");

        viewModel.Confirmation = ConfirmDeleteDialogViewModel.RequiredPhrase;

        Assert.True(viewModel.CanDelete);
    }

    [Theory]
    [InlineData("Delete")]
    [InlineData("DELETE")]
    [InlineData("delete ")]
    [InlineData(" delete")]
    [InlineData("del")]
    [InlineData("deleted")]
    [InlineData("")]
    public void NothingElseEnablesIt(string typed)
    {
        var viewModel = ConfirmDeleteDialogViewModel.ForFolder("Code");

        viewModel.Confirmation = typed;

        Assert.False(viewModel.CanDelete);
    }

    [Fact]
    public void ClearingThePhraseDisablesItAgain()
    {
        var viewModel = ConfirmDeleteDialogViewModel.ForFolder("Code");
        viewModel.Confirmation = ConfirmDeleteDialogViewModel.RequiredPhrase;

        viewModel.Confirmation = string.Empty;

        Assert.False(viewModel.CanDelete);
    }

    [Fact]
    public void CanDeleteIsAnnouncedSoTheButtonFollowsIt()
    {
        var viewModel = ConfirmDeleteDialogViewModel.ForFolder("Code");
        var announced = new List<string?>();
        viewModel.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        viewModel.Confirmation = ConfirmDeleteDialogViewModel.RequiredPhrase;

        Assert.Contains(nameof(ConfirmDeleteDialogViewModel.CanDelete), announced);
    }

    [Fact]
    public void AFolderIsNamedAndItsDeleteCalledFinal()
    {
        var viewModel = ConfirmDeleteDialogViewModel.ForFolder("Code");

        Assert.Equal("Delete Folder", viewModel.Title);
        Assert.Equal("Code", viewModel.ItemName);
        Assert.Contains("cannot be undone", viewModel.Message);
    }

    [Fact]
    public void AVirtualVolumeIsNamedAndItsDeleteCalledFinal()
    {
        var viewModel = ConfirmDeleteDialogViewModel.ForVirtualVolume("test", 3);

        Assert.Equal("Delete Virtual Volume", viewModel.Title);
        Assert.Equal("test", viewModel.ItemName);
        Assert.Contains("cannot be undone", viewModel.Message);
    }

    [Theory]
    [InlineData(0, "It holds no folders.")]
    [InlineData(1, "The 1 folder it holds is deleted with it.")]
    [InlineData(2, "The 2 folders it holds are deleted with it.")]
    public void TheVolumeMessageCountsWhatGoesWithIt(int folderCount, string expected)
    {
        var viewModel = ConfirmDeleteDialogViewModel.ForVirtualVolume("test", folderCount);

        Assert.Contains(expected, viewModel.Message);
    }
}
