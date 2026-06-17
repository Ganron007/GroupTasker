using Avalonia.Controls;
using GroupTasker.UI.ViewModels;

namespace GroupTasker.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    /// <summary>
    /// When true, closing the window hides it instead of exiting the app. Used
    /// in tray mode so the user can dismiss the window but the app keeps
    /// running in the system tray.
    /// </summary>
    public bool CloseHidesToTray { get; set; }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            await vm.LoadGroupsCommand.ExecuteAsync(null);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // In tray mode, swallow the close and just hide the window. The app
        // stays alive in the system tray; the user reopens via the tray menu
        // or quits via "Exit" on the tray.
        if (CloseHidesToTray)
        {
            e.Cancel = true;
            Hide();
        }
    }
}

