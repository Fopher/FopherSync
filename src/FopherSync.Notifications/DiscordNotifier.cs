using System.Text;
using System.Text.Json;
using FopherSync.Core.Models;

namespace FopherSync.Notifications;

public class DiscordNotifier : INotifier
{
    private readonly HttpClient _httpClient;
    private readonly NotificationSettings _settings;

    public string ChannelName => "Discord Webhook";

    public DiscordNotifier(NotificationSettings settings, HttpClient? httpClient = null)
    {
        _settings = settings;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<(bool Success, string Message)> SendNotificationAsync(NotificationPayload payload, CancellationToken cancellationToken = default)
    {
        if (!_settings.DiscordEnabled || string.IsNullOrWhiteSpace(_settings.DiscordWebhookUrl))
            return (false, "Discord notifications are disabled or webhook URL is empty.");

        var isFailure = payload.Status == JobStatus.Failed || payload.Status == JobStatus.AbortedBySafetyGuard;
        var isWarning = payload.Status == JobStatus.Warning;

        // Discord embed color (decimal integer):
        // Green: 3066993, Orange: 15105570, Red: 15158332
        var color = isFailure ? 15158332 : (isWarning ? 15105570 : 3066993);
        var statusText = isFailure ? "❌ FAILED" : (isWarning ? "⚠️ WARNING" : "✅ SUCCESS");

        var embed = new
        {
            title = $"{statusText} — {payload.JobName}",
            description = payload.Summary,
            color = color,
            fields = new[]
            {
                new { name = "Duration", value = $"{payload.DurationSeconds:F1}s", @inline = true },
                new { name = "Files Copied", value = $"{payload.FilesCopied:N0}", @inline = true },
                new { name = "Transferred", value = NotificationPayload.FormatBytes(payload.BytesTransferred), @inline = true },
                new { name = "Errors", value = payload.ErrorCount.ToString(), @inline = true },
                new { name = "Dry Run", value = payload.IsDryRun ? "Yes" : "No", @inline = true }
            },
            footer = new
            {
                text = "FopherSync"
            },
            timestamp = payload.Timestamp.ToString("o")
        };

        var payloadObject = new
        {
            username = "FopherSync",
            embeds = new[] { embed }
        };

        try
        {
            var json = JsonSerializer.Serialize(payloadObject);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_settings.DiscordWebhookUrl, content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return (true, "Discord notification delivered successfully.");
            }

            return (false, $"Discord returned HTTP {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, $"Discord webhook error: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> SendTestNotificationAsync(CancellationToken cancellationToken = default)
    {
        var testPayload = new NotificationPayload
        {
            JobName = "Test Notification",
            Status = JobStatus.Success,
            Summary = "Your Discord webhook is linked and working with FopherSync!",
            Timestamp = DateTime.UtcNow,
            DurationSeconds = 0.5,
            FilesCopied = 1,
            BytesTransferred = 512,
            IsDryRun = true
        };

        return await SendNotificationAsync(testPayload, cancellationToken);
    }
}
