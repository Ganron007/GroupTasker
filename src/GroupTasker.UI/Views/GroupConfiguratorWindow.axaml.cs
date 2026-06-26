using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using GroupTasker.UI.ViewModels;

namespace GroupTasker.UI.Views;

public partial class GroupConfiguratorWindow : Window
{
    private bool _isDragging;
    private Point _dragStartPoint;
    private ShortcutViewModel? _draggedItem;
    private ContentPresenter? _draggedContainer;
    private int _lastDropIndex = -1;
    private const double DragThreshold = 5.0;

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
    // UX: grab an item → it visually follows the cursor (TranslateTransform
    // on the container) → a blue drop-indicator line shows exactly where it
    // will land → release to drop. The collection is NOT modified during
    // drag so there are no stale container references.

    private void OnShortcutPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border) return;
        if (border.DataContext is not ShortcutViewModel vm) return;
        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;

        _dragStartPoint = e.GetPosition(ShortcutsItemsControl);
        _draggedItem = vm;

        var index = ShortcutsItemsControl.Items.IndexOf(vm);
        _draggedContainer = index >= 0
            ? ShortcutsItemsControl.ContainerFromIndex(index) as ContentPresenter
            : null;

        _isDragging = false;
        _lastDropIndex = -1;

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
            StartDragVisual();
        }

        if (_isDragging)
        {
            // Item follows the cursor
            if (_draggedContainer is not null)
                _draggedContainer.RenderTransform = new TranslateTransform(0, diff.Y);

            // Update the blue drop-indicator line
            UpdateDropIndicator(currentPoint);

            e.Handled = true;
        }
    }

    private void OnShortcutPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggedItem is null) return;

        var dropIndex = -1;
        if (_isDragging)
        {
            var dropPoint = e.GetPosition(ShortcutsItemsControl);
            dropIndex = CalculateDropIndex(dropPoint);
        }

        CleanupDrag();

        if (dropIndex >= 0 && DataContext is GroupConfiguratorViewModel vm)
            vm.MoveToIndex(_draggedItem, dropIndex);

        e.Handled = true;
    }

    private void StartDragVisual()
    {
        if (_draggedContainer is null) return;
        _draggedContainer.Opacity = 0.7;
        _draggedContainer.ZIndex = 100;
    }

    private void UpdateDropIndicator(Point point)
    {
        var dropIndex = CalculateDropIndex(point);
        if (dropIndex == _lastDropIndex) return;
        _lastDropIndex = dropIndex;

        var items = ShortcutsItemsControl.Items;
        var count = items.Count;

        // Position the indicator above the target item, or below the last.
        double y;
        if (dropIndex >= count && count > 0)
        {
            if (ShortcutsItemsControl.ContainerFromIndex(count - 1) is ContentPresenter last)
                y = last.Bounds.Bottom;
            else
                y = point.Y;
        }
        else if (dropIndex >= 0 &&
                 ShortcutsItemsControl.ContainerFromIndex(dropIndex) is ContentPresenter target)
        {
            y = target.Bounds.Top;
        }
        else
        {
            DropIndicator.IsVisible = false;
            return;
        }

        DropIndicator.Margin = new Thickness(0, y - 2, 0, 0);
        DropIndicator.IsVisible = true;
    }

    private void CleanupDrag()
    {
        if (_draggedContainer is not null)
        {
            _draggedContainer.Opacity = 1.0;
            _draggedContainer.ZIndex = 0;
            _draggedContainer.RenderTransform = null;
        }
        DropIndicator.IsVisible = false;
        _lastDropIndex = -1;
        _draggedContainer = null;
        _isDragging = false;
        _draggedItem = null;
    }

    /// <summary>
    /// Walk the list top-to-bottom. Return the index of the first item whose
    /// vertical midpoint is below the pointer. If the pointer is below all
    /// midpoints, return <see cref="Items.Count"/> (drop after the last item).
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

        return items.Count;
    }
}
