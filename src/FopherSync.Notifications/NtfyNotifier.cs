using System.Net.Http.Headers;
using System.Text;
using FopherSync.Core.Models;

namespace FopherSync.Notifications;

public class NtfyNotifier : INotifier
{
    private readonly HttpClient _httpClient;
    private readonly NotificationSettings _settings;

    public string ChannelName => "ntfy.sh (Phone Push)";

    public NtfyNotifier(NotificationSettings settings, HttpClient? httpClient = null)
    {
        _settings = settings;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<(bool Success, string Message)> SendNotificationAsync(NotificationPayload payload, CancellationToken cancellationToken = default)
    {
        if (!_settings.NtfyEnabled || string.IsNullOrWhiteSpace(_settings.NtfyTopic))
            return (false, "ntfy.sh is disabled or topic is empty.");

        var server = string.IsNullOrWhiteSpace(_settings.NtfyServerUrl) ? "https://ntfy.sh" : _settings.NtfyServerUrl.TrimEnd('/');
        var url = $"{server}/{_settings.NtfyTopic.Trim()}";

        var isFailure = payload.Status == JobStatus.Failed || payload.Status == JobStatus.AbortedBySafetyGuard;
        var isWarning = payload.Status == JobStatus.Warning;

        var priority = isFailure ? "urgent" : (isWarning ? "high" : "default");
        var tags = isFailure ? "x,rotating_light" : (isWarning ? "warning" : "white_check_mark,floppy_disk");
        var title = $"[FopherSync] {payload.JobName} - {(isFailure ? "FAILED" : (isWarning ? "WARNING" : "SUCCESS"))}";

        var bodyBuilder = new StringBuilder();
        bodyBuilder.AppendLine(payload.Summary);
        bodyBuilder.AppendLine();
        bodyBuilder.AppendLine($"• Duration: {payload.DurationSeconds:F1}s");
        bodyBuilder.AppendLine($"• Files Copied: {payload.FilesCopied:N0}");
        bodyBuilder.AppendLine($"• Transferred: {NotificationPayload.FormatBytes(payload.BytesTransferred)}");
        if (payload.ErrorCount > 0)
            bodyBuilder.AppendLine($"• Errors: {payload.ErrorCount}");
        if (payload.IsDryRun)
            bodyBuilder.AppendLine("• (Simulated / Dry Run)");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(bodyBuilder.ToString(), Encoding.UTF8, "text/plain")
            };

            request.Headers.Add("Title", title);
            request.Headers.Add("Priority", priority);
            request.Headers.Add("Tags", tags);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return (true, "Push notification sent successfully.");
            }

            return (false, $"ntfy.sh returned HTTP {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to send ntfy push: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> SendTestNotificationAsync(CancellationToken cancellationToken = default)
    {
        var testPayload = new NotificationPayload
        {
            JobName = "Test Connection",
            Status = JobStatus.Success,
            Summary = "FopherSync push notifications are properly configured!",
            Timestamp = DateTime.UtcNow,
            DurationSeconds = 1.2,
            FilesCopied = 1,
            BytesTransferred = 1024,
            IsDryRun = true
        };

        return await SendNotificationAsync(testPayload, cancellationToken);
    }
}
