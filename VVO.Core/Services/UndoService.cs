namespace VVO.Core.Services;

using UndoableCommand = (Func<Task> ExecuteAsync, Func<Task> UndoAsync);

public class UndoService : IUndoService
{
    private readonly Stack<UndoableCommand> _undoStack = new();
    private readonly Stack<UndoableCommand> _redoStack = new();

    public event EventHandler? Changed;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public async Task ExecuteAsync(Func<Task> execute, Func<Task> undo)
    {
        await execute();

        _undoStack.Push((execute, undo));
        _redoStack.Clear();

        RaiseChanged();
    }

    public async Task UndoAsync()
    {
        if (_undoStack.Count == 0)
            return;

        var command = _undoStack.Pop();
        await command.UndoAsync();

        _redoStack.Push(command);
        RaiseChanged();
    }

    public async Task RedoAsync()
    {
        if (_redoStack.Count == 0)
            return;

        var command = _redoStack.Pop();
        await command.ExecuteAsync();

        _undoStack.Push(command);
        RaiseChanged();
    }

    public void Clear()
    {
        if (_undoStack.Count == 0 && _redoStack.Count == 0)
            return;

        _undoStack.Clear();
        _redoStack.Clear();

        RaiseChanged();
    }

    private void RaiseChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
