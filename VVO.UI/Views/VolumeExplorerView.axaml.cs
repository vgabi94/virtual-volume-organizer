using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using VVO.UI.Messages;
using VVO.UI.ViewModels;

namespace VVO.UI.Views;

public partial class VolumeExplorerView : UserControl, IRecipient<FocusFileSearchMessage>
{
    private const string LocationHeader = "Location";

    private readonly DataGridColumn? _locationColumn;

    private VolumeExplorerViewModel? _viewModel;

    public VolumeExplorerView()
    {
        InitializeComponent();

        _locationColumn = FileGrid.Columns.FirstOrDefault(column => Equals(column.Header, LocationHeader));
        DataContextChanged += OnViewDataContextChanged;

        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    // Where the caret goes is the view's business, so Find is answered here rather than in the
    // view model that owns the term itself
    public void Receive(FocusFileSearchMessage message)
    {
        FileSearchBox.Focus();
        FileSearchBox.SelectAll();
    }

    // The DataGrid has no row activation command, and the event fires for the header and
    // empty space too, so the row has to be located before the item can be opened.
    private void OnFileGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source)
            return;

        if (source.FindAncestorOfType<DataGridRow>()?.DataContext is not FileItem item)
            return;

        if (DataContext is VolumeExplorerViewModel viewModel && viewModel.OpenItemCommand.CanExecute(item))
        {
            viewModel.OpenItemCommand.Execute(item);
        }
    }

    // A right-click does not move the selection on its own, which would leave the copy commands
    // acting on whatever row was left selected rather than the one under the pointer.
    private void OnFileGridContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Visual source)
            return;

        if (source.FindAncestorOfType<DataGridRow>()?.DataContext is FileItem item)
        {
            FileGrid.SelectedItem = item;
        }
    }

    // Columns sit outside the visual tree and never inherit a DataContext, so the Location
    // column cannot bind its own visibility and has to be driven from here. Naming it in XAML
    // is no help either: only Controls get a generated field.
    private void OnViewDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as VolumeExplorerViewModel;

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateLocationColumn();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VolumeExplorerViewModel.IsFlatMode))
        {
            UpdateLocationColumn();
        }
    }

    private void UpdateLocationColumn()
    {
        if (_locationColumn != null)
        {
            _locationColumn.IsVisible = _viewModel is { IsFlatMode: true };
        }
    }
}
