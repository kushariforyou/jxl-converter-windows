using Avalonia.Controls;
using Avalonia.Interactivity;

namespace JxlConverter;

/// Yes/No confirmation; closes with true only when the user picks Yes. "No" is the default button.
public partial class ConfirmDialog : Window
{
    public ConfirmDialog() : this("", "", "Yes", "No") { }

    public ConfirmDialog(string heading, string message, string yes, string no)
    {
        InitializeComponent();
        Title = heading;
        HeadingText.Text = heading;
        MessageText.Text = message;
        YesButton.Content = yes;
        NoButton.Content = no;
    }

    private void Yes_Click(object? sender, RoutedEventArgs e) => Close(true);
    private void No_Click(object? sender, RoutedEventArgs e) => Close(false);
}
