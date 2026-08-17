using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VVO.Core.Services;

namespace VVO.UI.ViewModels;

public abstract partial class ViewModelBase : ObservableRecipient
{
    protected readonly IUndoService _undoManager;

    protected ViewModelBase(IUndoService undoManager)
    {
        _undoManager = undoManager;

        // A step recorded by one view model has to reach the undo buttons of the others
        _undoManager.Changed += (_, _) => Dispatcher.UIThread.Post(NotifyUndoRedoState);
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        await _undoManager.UndoAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private async Task RedoAsync()
    {
        await _undoManager.RedoAsync();
    }

    private bool CanUndo() => _undoManager.CanUndo;
    private bool CanRedo() => _undoManager.CanRedo;

    private void NotifyUndoRedoState()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }
}
