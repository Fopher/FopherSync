using FopherSync.Core.Models;

namespace FopherSync.Notifications;

public class NotificationDispatcher
{
    private readonly NotificationSettings _settings;
    private readonly List<INotifier> _notifiers = new();

    public NotificationDispatcher(NotificationSettings settings, HttpClient? httpClient = null)
    {
        _settings = settings;
        _notifiers.Add(new NtfyNotifier(settings, httpClient));
        _notifiers.Add(new DiscordNotifier(settings, httpClient));
        _notifiers.Add(new SmtpNotifier(settings));
    }

    public async Task<List<(string Channel, bool Success, string Message)>> DispatchAsync(
        JobRunRecord record,
        CancellationToken cancellationToken = default)
    {
        var results = new List<(string, bool, string)>();

        // Check if we should notify for this status
        var shouldNotify = record.Status switch
        {
            JobStatus.Success => _settings.NotifyOnSuccess,
            JobStatus.Warning => _settings.NotifyOnWarning,
            JobStatus.Failed => _settings.NotifyOnFailure,
            JobStatus.AbortedBySafetyGuard => _settings.NotifyOnFailure,
            _ => false
        };

        if (!shouldNotify) return results;

        var payload = NotificationPayload.FromRecord(record);

        foreach (var notifier in _notifiers)
        {
            try
            {
                var (success, msg) = await notifier.SendNotificationAsync(payload, cancellationToken);
                results.Add((notifier.ChannelName, success, msg));
            }
            catch (Exception ex)
            {
                results.Add((notifier.ChannelName, false, ex.Message));
            }
        }

        return results;
    }
}
