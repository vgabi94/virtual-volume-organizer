using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

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

        if (change.Property == WindowStateProperty)
        {
            var fullScreen = change.GetNewValue<WindowState>() == WindowState.FullScreen;
            Classes.Set("FullScreen", fullScreen);

            if (fullScreen)
            {
                _beforeFullScreen = change.GetOldValue<WindowState>();

                // The full screen caption button lives outside the window, and keeps the focus
                // after it is clicked: Esc pressed there would never reach the window
                if (FocusManager?.GetFocusedElement() is not Visual focused || !this.IsVisualAncestorOf(focused))
                {
                    Focus();
                }
            }
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