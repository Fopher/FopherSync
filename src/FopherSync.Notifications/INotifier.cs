using FopherSync.Core.Models;

namespace FopherSync.Notifications;

public class NotificationPayload
{
    public required string JobName { get; init; }
    public required JobStatus Status { get; init; }
    public required string Summary { get; init; }
    public required DateTime Timestamp { get; init; }
    public double DurationSeconds { get; init; }
    public long FilesCopied { get; init; }
    public long BytesTransferred { get; init; }
    public long ErrorCount { get; init; }
    public string? LogFilePath { get; init; }
    public string? ErrorDetails { get; init; }
    public bool IsDryRun { get; init; }

    public static NotificationPayload FromRecord(JobRunRecord record)
    {
        return new NotificationPayload
        {
            JobName = record.JobName,
            Status = record.Status,
            Summary = record.ExitSummary,
            Timestamp = record.EndTime,
            DurationSeconds = record.DurationSeconds,
            FilesCopied = record.FilesCopied,
            BytesTransferred = record.BytesTransferred,
            ErrorCount = record.ErrorCount,
            LogFilePath = record.LogFilePath,
            ErrorDetails = record.ErrorDetails,
            IsDryRun = record.WasDryRun
        };
    }

    public static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}

public interface INotifier
{
    string ChannelName { get; }
    Task<(bool Success, string Message)> SendNotificationAsync(NotificationPayload payload, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> SendTestNotificationAsync(CancellationToken cancellationToken = default);
}
