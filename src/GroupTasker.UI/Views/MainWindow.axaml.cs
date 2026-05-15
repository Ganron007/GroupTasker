using Avalonia.Controls;
using GroupTasker.UI.ViewModels;

namespace GroupTasker.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            await vm.LoadGroupsCommand.ExecuteAsync(null);
    }
}
