using System.Diagnostics;
using System.Windows;

namespace FopherSync.Wpf;

public partial class NtfyHelpGuideWindow : Window
{
    public NtfyHelpGuideWindow()
    {
        InitializeComponent();
    }

    private void OnOpenNtfyWebClick(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://ntfy.sh");
    }

    private void OnOpenSelfHostDocsClick(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://docs.ntfy.sh/install/");
    }

    private void OnHyperlinkNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        OpenUrl(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
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
