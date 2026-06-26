using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using GroupTasker.UI.ViewModels;

namespace GroupTasker.UI.Views;

public partial class GroupConfiguratorWindow : Window
{
    private bool _isDragging;
    private Point _dragStartPoint;
    private ShortcutViewModel? _draggedItem;
    private const double DragThreshold = 8.0;

    public GroupConfiguratorWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is GroupConfiguratorViewModel vm)
                vm.HostWindow = this;
        };
    }

    // ── Drag-and-drop reorder ───────────────────────────────────────
    //
    // Design: the item is NOT moved during drag.  Instead we dim it,
    // track the pointer, and move it to the final target index on
    // release.  This avoids the stale-container-reference bug that
    // occurred when MoveToIndex was called mid-drag (Avalonia recycles
    // ContentPresenters after a collection change, so _draggedBorder
    // ended up pointing at the wrong item).

    private void OnShortcutPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border) return;
        if (border.DataContext is not ShortcutViewModel vm) return;
        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;

        _dragStartPoint = e.GetPosition(ShortcutsItemsControl);
        _draggedItem = vm;
        _isDragging = false;

        e.Handled = true;
    }

    private void OnShortcutPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedItem is null) return;

        if (!e.GetCurrentPoint(ShortcutsItemsControl).Properties.IsLeftButtonPressed)
        {
            CleanupDrag();
            return;
        }

        var currentPoint = e.GetPosition(ShortcutsItemsControl);
        var diff = currentPoint - _dragStartPoint;

        if (!_isDragging && (Math.Abs(diff.X) > DragThreshold || Math.Abs(diff.Y) > DragThreshold))
        {
            _isDragging = true;
            SetItemOpacity(_draggedItem, 0.3);
        }

        if (_isDragging)
            e.Handled = true;
    }

    private void OnShortcutPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggedItem is null) return;

        if (_isDragging)
        {
            var dropPoint = e.GetPosition(ShortcutsItemsControl);
            var dropIndex = CalculateDropIndex(dropPoint);

            if (DataContext is GroupConfiguratorViewModel vm)
                vm.MoveToIndex(_draggedItem, dropIndex);
        }

        CleanupDrag();
        e.Handled = true;
    }

    private void CleanupDrag()
    {
        if (_draggedItem is not null)
            SetItemOpacity(_draggedItem, 1.0);
        _isDragging = false;
        _draggedItem = null;
    }

    private void SetItemOpacity(ShortcutViewModel item, double opacity)
    {
        var index = ShortcutsItemsControl.Items.IndexOf(item);
        if (index < 0) return;
        if (ShortcutsItemsControl.ContainerFromIndex(index) is ContentPresenter container)
            container.Opacity = opacity;
    }

    /// <summary>
    /// Walk the list top-to-bottom and return the index of the first item
    /// whose vertical midpoint is below the pointer.  If the pointer is
    /// below all items, return the last index.
    /// </summary>
    private int CalculateDropIndex(Point point)
    {
        var items = ShortcutsItemsControl.Items;
        if (items.Count == 0) return 0;

        for (var i = 0; i < items.Count; i++)
        {
            if (ShortcutsItemsControl.ContainerFromIndex(i) is not ContentPresenter container)
                continue;
            var midY = container.Bounds.Top + container.Bounds.Height / 2;
            if (point.Y < midY)
                return i;
        }

        return items.Count - 1;
    }
}
