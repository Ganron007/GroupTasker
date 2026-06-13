using System;
using System.Threading.Tasks;
using Avalonia.Controls;
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
