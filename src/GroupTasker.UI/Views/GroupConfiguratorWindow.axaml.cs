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
    private Border? _draggedBorder;
    private int _dragStartIndex;
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

    private void OnShortcutPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border) return;
        if (border.DataContext is not ShortcutViewModel vm) return;
        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;

        _dragStartPoint = e.GetPosition(ShortcutsItemsControl);
        _draggedItem = vm;
        _draggedBorder = border;
        _dragStartIndex = ShortcutsItemsControl.Items.IndexOf(vm);
        _isDragging = false;

        e.Handled = true;
    }

    private void OnShortcutPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedItem is null || _draggedBorder is null) return;
        if (!e.GetCurrentPoint(ShortcutsItemsControl).Properties.IsLeftButtonPressed) return;

        var currentPoint = e.GetPosition(ShortcutsItemsControl);
        var diff = currentPoint - _dragStartPoint;

        if (!_isDragging && (Math.Abs(diff.X) > DragThreshold || Math.Abs(diff.Y) > DragThreshold))
        {
            _isDragging = true;
            _draggedBorder.Opacity = 0.5;
            _draggedBorder.ZIndex = 100;
            _draggedBorder.RenderTransform = new TranslateTransform(diff.X, diff.Y);
        }

        if (_isDragging)
        {
            _draggedBorder.RenderTransform = new TranslateTransform(diff.X, diff.Y);

            var dropIndex = CalculateDropIndex(currentPoint);
            var items = ShortcutsItemsControl.Items;
            var vm = DataContext as GroupConfiguratorViewModel;
            if (vm is not null && _draggedItem is not null)
            {
                var currentIdx = items.IndexOf(_draggedItem);
                if (currentIdx >= 0 && dropIndex != currentIdx)
                {
                    vm.MoveToIndex(_draggedItem, dropIndex);
                    _dragStartPoint = e.GetPosition(ShortcutsItemsControl);
                    _dragStartIndex = dropIndex;
                }
            }

            e.Handled = true;
        }
    }

    private void OnShortcutPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggedItem is null || _draggedBorder is null) return;

        if (_isDragging)
        {
            _draggedBorder.Opacity = 1.0;
            _draggedBorder.ZIndex = 0;
            _draggedBorder.RenderTransform = null;
        }

        _isDragging = false;
        _draggedItem = null;
        _draggedBorder = null;
        e.Handled = true;
    }

    private int CalculateDropIndex(Point currentPoint)
    {
        var items = ShortcutsItemsControl.Items;
        if (items.Count == 0) return 0;

        for (var i = 0; i < items.Count; i++)
        {
            var container = ShortcutsItemsControl.ContainerFromIndex(i) as ContentPresenter;
            if (container?.RenderTransform is TranslateTransform transform)
            {
                var itemTop = container.Bounds.Top + transform.Y;
                var itemHeight = container.Bounds.Height;
                if (currentPoint.Y < itemTop + itemHeight / 2)
                    return i;
            }
            else if (container is not null)
            {
                var itemTop = container.Bounds.Top;
                var itemHeight = container.Bounds.Height;
                if (currentPoint.Y < itemTop + itemHeight / 2)
                    return i;
            }
        }

        return items.Count - 1;
    }
}
