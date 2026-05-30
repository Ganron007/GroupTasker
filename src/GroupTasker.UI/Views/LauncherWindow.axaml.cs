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
