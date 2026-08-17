namespace VVO.Core.Services;

public interface IUndoService
{
    /// <summary>
    /// Raised whenever the stacks change. Each view model owns its own undo and redo commands
    /// over this one service, so they all have to be told when the state moves.
    /// </summary>
    event EventHandler? Changed;

    bool CanUndo { get; }
    bool CanRedo { get; }

    Task ExecuteAsync(Func<Task> execute, Func<Task> undo);
    Task UndoAsync();
    Task RedoAsync();

    /// <summary>
    /// Drops both stacks, for when something has happened that the recorded steps can no
    /// longer be replayed against.
    /// </summary>
    void Clear();
}
