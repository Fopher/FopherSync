using System.Windows;

namespace FopherSync.Wpf;

public partial class EmailErrorDialog : Window
{
    private readonly string _rawErrorMessage;

    public EmailErrorDialog(string rawErrorMessage, bool isAuthError = true)
    {
        InitializeComponent();
        _rawErrorMessage = rawErrorMessage;

        var cleanDetail = CleanTechnicalMessage(rawErrorMessage);

        if (!isAuthError)
        {
            TxtTitle.Text = "Email Connection Failed";
            TxtMessage.Text = "Could not deliver email to the SMTP server. Please check the host address, port, and SSL/TLS toggle.";
        }
        else
        {
            TxtTitle.Text = "SMTP Authentication Failed (Bad Credentials)";
            TxtMessage.Text = "Your username or password was not accepted by the mail server. Gmail, Yahoo, and Outlook reject regular login passwords and require a 16-character App Password.";
        }

        if (!string.IsNullOrWhiteSpace(cleanDetail))
        {
            TxtTechnicalDetail.Text = cleanDetail;
            TxtTechnicalDetail.Visibility = Visibility.Visible;
        }
        else
        {
            TxtTechnicalDetail.Visibility = Visibility.Collapsed;
        }
    }

    private static string CleanTechnicalMessage(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
            return string.Empty;

        var clean = rawMessage.Trim();

        // Strip common exception prefixes
        if (clean.StartsWith("SMTP send error:", StringComparison.OrdinalIgnoreCase))
            clean = clean.Substring("SMTP send error:".Length).Trim();

        if (clean.StartsWith("The SMTP server has unexpectedly disconnected:", StringComparison.OrdinalIgnoreCase))
            clean = clean.Substring("The SMTP server has unexpectedly disconnected:".Length).Trim();

        // Strip verbose trailing documentation URLs (e.g. "For more information, go to https://...")
        var infoIdx = clean.IndexOf("For more information", StringComparison.OrdinalIgnoreCase);
        if (infoIdx >= 0)
        {
            clean = clean.Substring(0, infoIdx).Trim().TrimEnd('.', ',');
        }

        var learnMoreIdx = clean.IndexOf("Learn more at", StringComparison.OrdinalIgnoreCase);
        if (learnMoreIdx >= 0)
        {
            clean = clean.Substring(0, learnMoreIdx).Trim().TrimEnd('.', ',');
        }

        // Remove trailing server session hashes/tokens (e.g., "- gsmtp" or raw socket codes)
        if (clean.EndsWith("- gsmtp", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring(0, clean.Length - 7).Trim();
        }

        if (string.IsNullOrWhiteSpace(clean))
            return string.Empty;

        return $"Server response: {clean}";
    }

    private void OnNeedHelpClick(object sender, RoutedEventArgs e)
    {
        var helpGuide = new EmailHelpGuideWindow();
        helpGuide.Owner = this.Owner ?? this;
        helpGuide.ShowDialog();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
