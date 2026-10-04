using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace VVO.UI.Views;

public partial class MainWindowView : Window
{
    // Where Esc puts the window back to: full screen can be entered from a maximised window
    private WindowState _beforeFullScreen = WindowState.Normal;

    public MainWindowView()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty
            && change.GetNewValue<WindowState>() == WindowState.FullScreen)
        {
            _beforeFullScreen = change.GetOldValue<WindowState>();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!e.Handled && e.Key == Key.Escape && WindowState == WindowState.FullScreen)
        {
            WindowState = _beforeFullScreen;
            e.Handled = true;
        }
    }
}