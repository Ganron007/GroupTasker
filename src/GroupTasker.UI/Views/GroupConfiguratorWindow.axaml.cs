using Avalonia.Controls;
using GroupTasker.UI.ViewModels;

namespace GroupTasker.UI.Views;

public partial class GroupConfiguratorWindow : Window
{
    public GroupConfiguratorWindow()
    {
        InitializeComponent();

        // Hand the window reference to the VM so it doesn't have to enumerate
        // Application.Windows looking for itself.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is GroupConfiguratorViewModel vm)
                vm.HostWindow = this;
        };
    }
}
