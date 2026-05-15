using System;
using Avalonia.Controls;
using Avalonia.Input;
using GroupTasker.UI.ViewModels;

namespace GroupTasker.UI.Views;

public partial class LauncherWindow : Window
{
    public LauncherWindow()
    {
        InitializeComponent();
        Deactivated += OnDeactivated;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Click-outside dismissal. If we ever wire up drag-and-drop again, the drag
        // operation will steal focus and this would close the popup mid-drag — guard
        // by suppressing this handler at drag start.
        Close();
    }

    private void OnDragStripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnShortcutPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border &&
            border.DataContext is LauncherShortcutViewModel vm &&
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            vm.LaunchCommand.Execute(null);
        }
    }
}
