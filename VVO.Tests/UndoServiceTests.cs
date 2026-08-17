using VVO.Core.Services;

namespace VVO.Tests;

public class UndoServiceTests
{
    [Fact]
    public void InitialState()
    {
        var service = new UndoService();

        Assert.False(service.CanUndo);
        Assert.False(service.CanRedo);
    }

    [Fact]
    public async Task ClearDropsBothStacks()
    {
        var service = new UndoService();

        await service.ExecuteAsync(() => Task.CompletedTask, () => Task.CompletedTask);
        await service.ExecuteAsync(() => Task.CompletedTask, () => Task.CompletedTask);
        await service.UndoAsync();

        service.Clear();

        Assert.False(service.CanUndo);
        Assert.False(service.CanRedo);
    }

    [Fact]
    public async Task EveryMoveAnnouncesItself()
    {
        var service = new UndoService();
        var announced = 0;
        service.Changed += (_, _) => announced++;

        await service.ExecuteAsync(() => Task.CompletedTask, () => Task.CompletedTask);
        await service.UndoAsync();
        await service.RedoAsync();
        service.Clear();

        Assert.Equal(4, announced);
    }

    [Fact]
    public void ClearIsSilentWhenThereIsNothingToDrop()
    {
        var service = new UndoService();
        var announced = 0;
        service.Changed += (_, _) => announced++;

        service.Clear();

        Assert.Equal(0, announced);
    }

    [Fact]
    public async Task ExecuteChangesFlags()
    {
        var service = new UndoService();

        await service.ExecuteAsync(() => Task.CompletedTask, () => Task.CompletedTask);

        Assert.True(service.CanUndo);
        Assert.False(service.CanRedo);
    }

    [Fact]
    public async Task UndoChangesFlags()
    {
        var service = new UndoService();
        await service.ExecuteAsync(() => Task.CompletedTask, () => Task.CompletedTask);

        await service.UndoAsync();

        Assert.False(service.CanUndo);
        Assert.True(service.CanRedo);
    }

    [Fact]
    public async Task RedoChangesFlags()
    {
        var service = new UndoService();
        await service.ExecuteAsync(() => Task.CompletedTask, () => Task.CompletedTask);
        await service.UndoAsync();

        await service.RedoAsync();

        Assert.True(service.CanUndo);
        Assert.False(service.CanRedo);
    }

    [Fact]
    public async Task ExecuteInvokesAction()
    {
        var invoked = false;
        var service = new UndoService();

        await service.ExecuteAsync(
            () => { invoked = true; return Task.CompletedTask; },
            () => Task.CompletedTask);

        Assert.True(invoked);
    }

    [Fact]
    public async Task UndoInvokesAction()
    {
        var undoInvoked = false;
        var service = new UndoService();
        await service.ExecuteAsync(
            () => Task.CompletedTask,
            () => { undoInvoked = true; return Task.CompletedTask; });

        await service.UndoAsync();

        Assert.True(undoInvoked);
    }

    [Fact]
    public async Task RedoReinvokesAction()
    {
        var executeCount = 0;
        var service = new UndoService();

        await service.ExecuteAsync(
            () => { executeCount++; return Task.CompletedTask; },
            () => Task.CompletedTask);

        await service.UndoAsync();
        await service.RedoAsync();

        Assert.Equal(2, executeCount);
    }

    [Fact]
    public async Task LinearStackHistory()
    {
        var order = new List<int>();
        var service = new UndoService();

        for (int i = 1; i <= 3; i++)
        {
            int action = i; // capture the loop variable by value
            await service.ExecuteAsync(
                () => { order.Add(action); return Task.CompletedTask; },
                () => { order.Add(-action); return Task.CompletedTask; });
        }

        Assert.Equal([1, 2, 3], order);

        order.Clear();
        await service.UndoAsync();
        await service.UndoAsync();
        await service.UndoAsync();

        Assert.Equal([-3, -2, -1], order);
    }

    [Fact]
    public async Task EmptyUndoNoOp()
    {
        var service = new UndoService();

        await service.UndoAsync();

        Assert.False(service.CanUndo);
        Assert.False(service.CanRedo);
    }

    [Fact]
    public async Task EmptyRedoNoOp()
    {
        var service = new UndoService();

        await service.RedoAsync();

        Assert.False(service.CanUndo);
        Assert.False(service.CanRedo);
    }

    [Fact]
    public async Task NewActionClearsRedo()
    {
        var service = new UndoService();
        await service.ExecuteAsync(() => Task.CompletedTask, () => Task.CompletedTask);
        await service.UndoAsync();
        Assert.True(service.CanRedo);

        await service.ExecuteAsync(() => Task.CompletedTask, () => Task.CompletedTask);

        Assert.False(service.CanRedo);

        await service.RedoAsync();
        Assert.True(service.CanUndo);
        Assert.False(service.CanRedo);
    }

    [Fact]
    public async Task ExecuteThrowsDoesNotPush()
    {
        var service = new UndoService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteAsync(
                () => throw new InvalidOperationException("execute failed"),
                () => Task.CompletedTask));

        Assert.False(service.CanUndo);
        Assert.False(service.CanRedo);
    }

    [Fact]
    public async Task UndoThrowsBubbleUp()
    {
        var service = new UndoService();
        await service.ExecuteAsync(
            () => Task.CompletedTask,
            () => throw new InvalidOperationException("undo failed"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UndoAsync());

        Assert.Equal("undo failed", ex.Message);
    }
}