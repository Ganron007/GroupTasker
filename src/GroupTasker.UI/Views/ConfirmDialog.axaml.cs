using Avalonia.Controls;

namespace GroupTasker.UI.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
        // Wire up the buttons unconditionally so the parameterless ctor (used by
        // the XAML compiler) produces a functional dialog as well.
        ConfirmBtn.Click += (_, _) => Close(true);
        CancelBtn.Click += (_, _) => Close(false);
    }

    public ConfirmDialog(string title, string message) : this()
    {
        TitleBlock.Text = title;
        MessageBlock.Text = message;
    }
}
