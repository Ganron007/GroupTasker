using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using GroupTasker.UI.ViewModels;

namespace GroupTasker.UI.Views;

public partial class LauncherWindow : Window
{
    private bool _isDragging;
    private bool _isLongPress;
    private Point _pressPoint;
    private LauncherShortcutViewModel? _pressedShortcut;
    private Border? _pressedBorder;
    private int _pressedIndex;
    private Timer? _longPressTimer;
    private const int LongPressMs = 300;
    private const double DragThreshold = 8.0;

    public LauncherWindow()
    {
        InitializeComponent();
        Deactivated += OnDeactivated;
        KeyDown += OnKeyDown;
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Focus the filter textbox so the user can immediately type to search.
        // Crucially, we do NOT re-focus on every filter change — that would steal
        // focus from the textbox on every keystroke and force the user to click
        // back into it to continue typing. The textbox keeps focus while typing;
        // Down arrow moves focus into the shortcut grid.
        Dispatcher.UIThread.Post(() => FilterTextBox.Focus(), DispatcherPriority.Input);
    }

    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (DataContext is LauncherViewModel vm && !string.IsNullOrEmpty(vm.Filter))
                {
                    vm.Filter = "";
                    FilterTextBox.Focus();
                }
                else
                {
                    Close();
                }
                e.Handled = true;
                break;

            case Key.Down:
                if (ShortcutsItemsControl.Items.Count > 0)
                    FocusShortcutAt(0);
                e.Handled = true;
                break;

            case Key.Enter:
                LaunchShortcutAt(0);
                e.Handled = true;
                break;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // If a filter is active, clear it; otherwise close. (The TextBox also handles
            // Esc on its own, so this branch is mostly hit when focus is on a shortcut.)
            if (DataContext is LauncherViewModel { Filter: { Length: > 0 } } vm)
            {
                vm.Filter = "";
                FilterTextBox.Focus();
            }
            else
            {
                Close();
            }
            e.Handled = true;
            return;
        }

        var count = ShortcutsItemsControl.Items.Count;
        if (count == 0) return;

        var current = GetFocusedShortcutIndex();
        if (current < 0) current = 0;

        int target = current;
        switch (e.Key)
        {
            case Key.Right: target = Math.Min(current + 1, count - 1); break;
            case Key.Left:  target = Math.Max(current - 1, 0); break;
            case Key.Down:  target = Math.Min(current + Columns, count - 1); break;
            case Key.Up:    target = Math.Max(current - Columns, 0); break;
            case Key.Enter:
            case Key.Space:
                LaunchShortcutAt(current);
                e.Handled = true;
                return;
        }

        if (target != current)
        {
            FocusShortcutAt(target);
            e.Handled = true;
        }
    }

    private const int Columns = 7;

    private int GetFocusedShortcutIndex()
    {
        for (var i = 0; i < ShortcutsItemsControl.Items.Count; i++)
        {
            if (ShortcutsItemsControl.ContainerFromIndex(i) is ContentPresenter cp
                && cp.Child is Border b
                && b.IsFocused)
                return i;
        }
        return -1;
    }

    private void FocusShortcutAt(int index)
    {
        if (ShortcutsItemsControl.ContainerFromIndex(index) is ContentPresenter cp
            && cp.Child is Border b)
            b.Focus();
    }

    private void LaunchShortcutAt(int index)
    {
        if (index < 0 || index >= ShortcutsItemsControl.Items.Count) return;
        if (ShortcutsItemsControl.Items[index] is LauncherShortcutViewModel vm)
            vm.LaunchCommand.Execute(null);
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_isDragging) return;
        Close();
    }

    private void OnDragStripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnShortcutPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border) return;
        if (border.DataContext is not LauncherShortcutViewModel vm) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _pressPoint = e.GetPosition(border);
        _pressedShortcut = vm;
        _pressedBorder = border;
        _pressedIndex = ShortcutsItemsControl.Items.IndexOf(vm);
        _isLongPress = false;
        _isDragging = false;

        _longPressTimer = new Timer(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_pressedShortcut is not null && _pressedBorder is not null)
                {
                    _isLongPress = true;
                    _pressedBorder.RenderTransform = new TranslateTransform(0, 0);
                    _pressedBorder.Opacity = 0.7;
                    _pressedBorder.ZIndex = 100;
                }
            });
        }, null, LongPressMs, Timeout.Infinite);

        e.Handled = true;
    }

    private void OnShortcutMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedShortcut is null || _pressedBorder is null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var currentPoint = e.GetPosition(ShortcutsItemsControl);
        var diff = currentPoint - _pressPoint;

        if (!_isLongPress)
        {
            if (Math.Abs(diff.X) > DragThreshold || Math.Abs(diff.Y) > DragThreshold)
            {
                CancelLongPress();
                return;
            }
            return;
        }

        _isDragging = true;
        _pressedBorder.RenderTransform = new TranslateTransform(
            currentPoint.X - _pressPoint.X,
            currentPoint.Y - _pressPoint.Y);

        e.Handled = true;
    }

    private void OnShortcutReleased(object? sender, PointerReleasedEventArgs e)
    {
        var wasLongPress = _isLongPress;
        var wasDragging = _isDragging;
        var pressed = _pressedShortcut;
        var pressedIdx = _pressedIndex;

        CancelLongPress();

        if (pressed is not null && _pressedBorder is not null)
        {
            _pressedBorder.Opacity = 1.0;
            _pressedBorder.ZIndex = 0;
            _pressedBorder.RenderTransform = null;
        }

        if (wasDragging && pressed is not null)
        {
            var dropIndex = CalculateDropIndex(e.GetPosition(ShortcutsItemsControl));
            if (pressedIdx != dropIndex && DataContext is LauncherViewModel vm)
            {
                vm.ReorderCommand.Execute((pressedIdx, dropIndex));
            }
        }
        else if (!wasLongPress && pressed is not null)
        {
            pressed.LaunchCommand.Execute(null);
        }

        _isDragging = false;
        _pressedShortcut = null;
        _pressedBorder = null;
        e.Handled = true;
    }

    private void CancelLongPress()
    {
        _longPressTimer?.Dispose();
        _longPressTimer = null;
    }

    private int CalculateDropIndex(Point currentPoint)
    {
        var items = ShortcutsItemsControl.Items;
        if (items.Count == 0) return 0;

        for (var i = 0; i < items.Count; i++)
        {
            var container = ShortcutsItemsControl.ContainerFromIndex(i) as ContentPresenter;
            if (container is null) continue;

            Rect bounds;
            if (container.RenderTransform is TranslateTransform transform)
            {
                bounds = new Rect(
                    container.Bounds.Left + transform.X,
                    container.Bounds.Top + transform.Y,
                    container.Bounds.Width,
                    container.Bounds.Height);
            }
            else
            {
                bounds = container.Bounds;
            }

            if (currentPoint.Y < bounds.Top + bounds.Height / 2)
                return i;
        }

        return items.Count - 1;
    }
}
