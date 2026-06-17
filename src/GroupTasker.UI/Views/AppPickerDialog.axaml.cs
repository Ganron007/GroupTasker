using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using GroupTasker.Domain.Interfaces;
using GroupTasker.UI.ViewModels;

namespace GroupTasker.UI.Views;

public partial class AppPickerDialog : Window
{
    public AppPickerDialog()
    {
        InitializeComponent();
    }

    /// <summary>Double-click on an app in the list: add it to the group (same as the Add button).</summary>
    private void OnAppDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is AppPickerViewModel vm && vm.SelectedApp is not null)
            vm.AddCommand.Execute(null);
    }

    /// <summary>
    /// Show the dialog and return the selected <see cref="DiscoveredApp"/>
    /// (or null if the user cancelled).
    /// </summary>
    public async Task<DiscoveredApp?> ShowPickerAsync(Window owner)
    {
        var vm = (AppPickerViewModel)DataContext!;
        var tcs = new TaskCompletionSource<DiscoveredApp?>();

        void OnCloseRequested(object? sender, bool accepted)
        {
            tcs.TrySetResult(accepted ? vm.DialogResult : null);
            Close(accepted);
        }

        vm.CloseRequested += OnCloseRequested;
        try
        {
            await ShowDialog(owner);
        }
        finally
        {
            vm.CloseRequested -= OnCloseRequested;
        }
        return await tcs.Task;
    }
}
