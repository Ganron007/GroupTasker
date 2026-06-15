using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using GroupTasker.UI.ViewModels;

namespace GroupTasker.UI.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is SettingsViewModel vm)
                await vm.LoadAsync();
        };
    }

    private void OnCloseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    public static Task ShowAsync(Window owner, SettingsViewModel vm)
    {
        var dialog = new SettingsDialog { DataContext = vm, Owner = owner };
        var tcs = new TaskCompletionSource();
        dialog.Closed += (_, _) => tcs.TrySetResult();
        dialog.Show();
        return tcs.Task;
    }
}
