using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using FopherSync.Core.Models;

namespace FopherSync.Notifications;

public class SmtpNotifier : INotifier
{
    private readonly NotificationSettings _settings;

    public string ChannelName => "Email (SMTP)";

    public SmtpNotifier(NotificationSettings settings)
    {
        _settings = settings;
    }

    public async Task<(bool Success, string Message)> SendNotificationAsync(NotificationPayload payload, CancellationToken cancellationToken = default)
    {
        if (!_settings.SmtpEnabled ||
            string.IsNullOrWhiteSpace(_settings.SmtpHost) ||
            string.IsNullOrWhiteSpace(_settings.RecipientEmail))
        {
            return (false, "SMTP is disabled or recipient email is missing.");
        }

        var isFailure = payload.Status == JobStatus.Failed || payload.Status == JobStatus.AbortedBySafetyGuard;
        var isWarning = payload.Status == JobStatus.Warning;
        var statusWord = isFailure ? "FAILED" : (isWarning ? "WARNING" : "SUCCESS");

        var message = new MimeMessage();
        var sender = !string.IsNullOrWhiteSpace(_settings.SenderEmail) 
            ? _settings.SenderEmail 
            : (!string.IsNullOrWhiteSpace(_settings.SmtpUsername) ? _settings.SmtpUsername : "fophersync@local");
        message.From.Add(new MailboxAddress("FopherSync", sender));
        message.To.Add(MailboxAddress.Parse(_settings.RecipientEmail));
        message.Subject = $"[FopherSync] {payload.JobName} - {statusWord}";

        var bodyBuilder = new BodyBuilder();

        // High-contrast clean dark/light card HTML
        var statusColor = isFailure ? "#D32F2F" : (isWarning ? "#F57C00" : "#388E3C");

        var html = $"""
        <div style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 1px solid #333; border-radius: 8px; overflow: hidden; background-color: #1e1e1e; color: #ffffff;">
            <div style="background-color: {statusColor}; padding: 16px 24px;">
                <h2 style="margin: 0; color: #ffffff; font-size: 20px;">FopherSync: {statusWord}</h2>
                <p style="margin: 4px 0 0 0; color: #f0f0f0; font-size: 14px;">Job: {payload.JobName}</p>
            </div>
            <div style="padding: 24px;">
                <p style="font-size: 16px; margin-top: 0;"><strong>Summary:</strong> {payload.Summary}</p>
                <table style="width: 100%; border-collapse: collapse; margin-top: 16px; font-size: 14px;">
                    <tr style="border-bottom: 1px solid #333;"><td style="padding: 8px 0; color: #aaa;">Timestamp:</td><td style="padding: 8px 0; text-align: right;">{payload.Timestamp:yyyy-MM-dd HH:mm:ss} UTC</td></tr>
                    <tr style="border-bottom: 1px solid #333;"><td style="padding: 8px 0; color: #aaa;">Duration:</td><td style="padding: 8px 0; text-align: right;">{payload.DurationSeconds:F1} seconds</td></tr>
                    <tr style="border-bottom: 1px solid #333;"><td style="padding: 8px 0; color: #aaa;">Files Copied:</td><td style="padding: 8px 0; text-align: right;">{payload.FilesCopied:N0}</td></tr>
                    <tr style="border-bottom: 1px solid #333;"><td style="padding: 8px 0; color: #aaa;">Transferred:</td><td style="padding: 8px 0; text-align: right;">{NotificationPayload.FormatBytes(payload.BytesTransferred)}</td></tr>
                    <tr style="border-bottom: 1px solid #333;"><td style="padding: 8px 0; color: #aaa;">Errors:</td><td style="padding: 8px 0; text-align: right; color: {(payload.ErrorCount > 0 ? "#FF5252" : "inherit")};">{payload.ErrorCount}</td></tr>
                    <tr><td style="padding: 8px 0; color: #aaa;">Dry Run:</td><td style="padding: 8px 0; text-align: right;">{(payload.IsDryRun ? "Yes (Simulated)" : "No")}</td></tr>
                </table>
                {(string.IsNullOrEmpty(payload.ErrorDetails) ? "" : $"<div style='margin-top: 20px; background-color: #2a1b1b; padding: 12px; border-radius: 4px; border-left: 4px solid #D32F2F;'><p style='margin: 0; color: #FF8A80; font-family: monospace; font-size: 13px;'>{payload.ErrorDetails}</p></div>")}
            </div>
            <div style="background-color: #141414; padding: 12px 24px; text-align: center; font-size: 12px; color: #777;">
                Sent automatically by FopherSync Desktop
            </div>
        </div>
        """;

        bodyBuilder.HtmlBody = html;
        bodyBuilder.TextBody = $"FopherSync: {payload.JobName} - {statusWord}\n\nSummary: {payload.Summary}\nDuration: {payload.DurationSeconds:F1}s\nFiles Copied: {payload.FilesCopied}\nErrors: {payload.ErrorCount}";

        message.Body = bodyBuilder.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            var secureOption = !_settings.SmtpUseSsl 
                ? SecureSocketOptions.None 
                : (_settings.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable);

            await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, secureOption, cancellationToken);

            if (!string.IsNullOrWhiteSpace(_settings.SmtpUsername) && !string.IsNullOrWhiteSpace(_settings.SmtpPassword))
            {
                await client.AuthenticateAsync(_settings.SmtpUsername, _settings.SmtpPassword, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            return (true, "Email alert sent successfully.");
        }
        catch (Exception ex)
        {
            return (false, $"SMTP send error: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> SendTestNotificationAsync(CancellationToken cancellationToken = default)
    {
        var testPayload = new NotificationPayload
        {
            JobName = "Test Email Verification",
            Status = JobStatus.Success,
            Summary = "Your SMTP email settings are correctly configured for FopherSync!",
            Timestamp = DateTime.UtcNow,
            DurationSeconds = 0.8,
            FilesCopied = 1,
            BytesTransferred = 2048,
            IsDryRun = true
        };

        return await SendNotificationAsync(testPayload, cancellationToken);
    }
}
