using Avalonia.Headless.XUnit;
using VVO.UI;

namespace VVO.UiTests;

/// <summary>
/// Reporting a failure. Every report comes out of a handler that has already handed back, so
/// nothing is left to catch what the report itself throws — in a running application that
/// reaches the dispatcher and takes the window down.
/// </summary>
public class ReportingTests : UiTestBase
{
    [AvaloniaFact]
    public async Task AFailureIsPutToTheUser()
    {
        await Logger.ShowErrorAsync(new InvalidOperationException("the drive is not there"));

        var (title, message) = Assert.Single(Told);
        Assert.Equal("Error", title);
        Assert.Contains("the drive is not there", message);
    }

    // What happens once the main window has gone: the message box has nothing to open onto
    [AvaloniaFact]
    public async Task AFailureThatCannotBePutToTheUserIsSwallowedRatherThanThrownAgain()
    {
        Dialogs.Told = null;

        await Logger.ShowErrorAsync(new InvalidOperationException("the drive is not there"));
    }

    [AvaloniaFact]
    public async Task AMessageThatCannotBeShownStillThrowsWhereSomethingIsWaitingForIt()
    {
        Dialogs.Told = null;

        await Assert.ThrowsAnyAsync<Exception>(() => Dialogs.TellAsync("Shrink Database", "1 KB reclaimed."));
    }
}
