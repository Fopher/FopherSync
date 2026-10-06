using System.Diagnostics;
using System.Windows;

namespace FopherSync.Wpf;

public partial class EmailHelpGuideWindow : Window
{
    public EmailHelpGuideWindow()
    {
        InitializeComponent();
    }

    private void OnOpenGoogleAppPasswordsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://myaccount.google.com/apppasswords") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open browser: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
