using Avalonia.Headless.XUnit;
using VVO.UI.ViewModels;

namespace VVO.UiTests;

/// <summary>
/// The two dialogs that go out to a picker of their own rather than taking everything the user
/// types.
/// </summary>
public class DialogViewModelTests : UiTestBase
{
    #region Choosing where a new database goes

    [AvaloniaFact]
    public async Task ThePickedPathBecomesTheDatabasePath()
    {
        var viewModel = new NewDatabaseDialogViewModel();
        PickFiles((_, _) => @"D:\Archive\Discs.vvo");

        await viewModel.SelectPathCommand.ExecuteAsync(Shell);

        Assert.Equal(@"D:\Archive\Discs.vvo", viewModel.DatabasePath);
    }

    [AvaloniaFact]
    public async Task ADatabaseWithNoNameYetTakesTheOneOffThePickedFile()
    {
        var viewModel = new NewDatabaseDialogViewModel();
        PickFiles((_, _) => @"D:\Archive\Discs.vvo");

        await viewModel.SelectPathCommand.ExecuteAsync(Shell);

        Assert.Equal("Discs", viewModel.CustomName);
    }

    [AvaloniaFact]
    public async Task ANameTheUserHasAlreadyTypedIsLeftAlone()
    {
        var viewModel = new NewDatabaseDialogViewModel { CustomName = "My Discs" };
        PickFiles((_, _) => @"D:\Archive\Discs.vvo");

        await viewModel.SelectPathCommand.ExecuteAsync(Shell);

        Assert.Equal("My Discs", viewModel.CustomName);
    }

    [AvaloniaFact]
    public async Task TheNameAlreadyTypedIsWhatTheSaveDialogSuggests()
    {
        var viewModel = new NewDatabaseDialogViewModel { CustomName = "My Discs" };

        string? suggested = null;
        PickFiles((name, _) => { suggested = name; return null; });

        await viewModel.SelectPathCommand.ExecuteAsync(Shell);

        Assert.Equal("My Discs.vvo", suggested);
    }

    [AvaloniaFact]
    public async Task ANameThatAlreadyEndsInVvoIsNotGivenASecondExtension()
    {
        var viewModel = new NewDatabaseDialogViewModel { CustomName = "Discs.VVO" };

        string? suggested = null;
        PickFiles((name, _) => { suggested = name; return null; });

        await viewModel.SelectPathCommand.ExecuteAsync(Shell);

        Assert.Equal("Discs.VVO", suggested);
    }

    [AvaloniaFact]
    public async Task WithNoNameYetTheDialogSuggestsOne()
    {
        string? suggested = null;
        PickFiles((name, _) => { suggested = name; return null; });

        await new NewDatabaseDialogViewModel().SelectPathCommand.ExecuteAsync(Shell);

        Assert.Equal("database.vvo", suggested);
    }

    [AvaloniaFact]
    public async Task PickingNoPathLeavesTheDialogAsItWas()
    {
        var viewModel = new NewDatabaseDialogViewModel { CustomName = "Discs" };
        PickFiles((_, _) => null);

        await viewModel.SelectPathCommand.ExecuteAsync(Shell);

        Assert.Empty(viewModel.DatabasePath);
        Assert.Equal("Discs", viewModel.CustomName);
    }

    [AvaloniaFact]
    public void ADatabaseNeedsBothANameAndAPlaceToGo()
    {
        var viewModel = new NewDatabaseDialogViewModel();
        Assert.False(viewModel.CanCreate);

        viewModel.CustomName = "Discs";
        Assert.False(viewModel.CanCreate);

        viewModel.DatabasePath = @"D:\Discs.vvo";
        Assert.True(viewModel.CanCreate);
    }

    #endregion

    #region Choosing what to compare against

    [AvaloniaFact]
    public async Task TheBrowsedFolderBecomesTheComparisonTarget()
    {
        var viewModel = new CompareTargetDialogViewModel([]);
        PickFolder(@"D:\Code");

        await viewModel.BrowseFolderCommand.ExecuteAsync(Shell);

        Assert.Equal(@"D:\Code", viewModel.LiveFolderPath);
        Assert.True(viewModel.CanCompare);
    }

    [AvaloniaFact]
    public async Task BrowsingToNoFolderLeavesTheDialogAsItWas()
    {
        var viewModel = new CompareTargetDialogViewModel([]);
        PickFolder(null);

        await viewModel.BrowseFolderCommand.ExecuteAsync(Shell);

        Assert.Empty(viewModel.LiveFolderPath);
        Assert.False(viewModel.CanCompare);
    }

    [AvaloniaFact]
    public async Task BrowsingToAFolderAbandonsTheCataloguedOneThatWasChosen()
    {
        var choice = new FolderChoice(Guid.NewGuid(), "Docs", @"D:\Docs");
        var viewModel = new CompareTargetDialogViewModel([choice]) { SelectedFolder = choice };
        PickFolder(@"D:\Code");

        await viewModel.BrowseFolderCommand.ExecuteAsync(Shell);

        Assert.Null(viewModel.SelectedFolder);
    }

    [AvaloniaFact]
    public void ChoosingACataloguedFolderAbandonsTheBrowsedOne()
    {
        var choice = new FolderChoice(Guid.NewGuid(), "Docs", @"D:\Docs");
        var viewModel = new CompareTargetDialogViewModel([choice]) { LiveFolderPath = @"D:\Code" };

        viewModel.SelectedFolder = choice;

        Assert.Empty(viewModel.LiveFolderPath);
        Assert.True(viewModel.CanCompare);
    }

    [AvaloniaFact]
    public void ThereIsNothingToCompareAgainstUntilOneOrTheOtherIsChosen()
    {
        Assert.False(new CompareTargetDialogViewModel([]).CanCompare);
    }

    #endregion
}
