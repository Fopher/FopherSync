namespace FopherSync.Core.Models;

public class JobRunRecord
{
    public long Id { get; set; }
    public string JobId { get; set; } = string.Empty;
    public string JobName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public double DurationSeconds { get; set; }

    public JobStatus Status { get; set; }
    public int ExitCode { get; set; }
    public string ExitSummary { get; set; } = string.Empty;

    public long FilesCopied { get; set; }
    public long FilesTotal { get; set; }
    public long BytesTransferred { get; set; }
    public long ErrorCount { get; set; }

    public bool WasDryRun { get; set; }
    public string LogFilePath { get; set; } = string.Empty;
    public string ErrorDetails { get; set; } = string.Empty;

    // Display / UI Helper Properties
    public string FormattedStartTime => Services.TimeZoneHelper.FormatDateTime(StartTime);
    public string FormattedDuration => $"{DurationSeconds:F1}s";
    public string FormattedTransferred => FormatBytes(BytesTransferred);
    public string StatusBadgeText => WasDryRun ? "SIMULATION" : Status.ToString().ToUpper();

    public string StatusBadgeBackground => (WasDryRun, Status) switch
    {
        (true, _) => "#1E293B",
        (_, JobStatus.Success) => "#143823",
        (_, JobStatus.Warning) => "#3B2D11",
        (_, JobStatus.Failed) => "#3F1818",
        (_, JobStatus.AbortedBySafetyGuard) => "#3F1818",
        _ => "#22222A"
    };

    public string StatusBadgeForeground => (WasDryRun, Status) switch
    {
        (true, _) => "#94A3B8",
        (_, JobStatus.Success) => "#34D399",
        (_, JobStatus.Warning) => "#FBBF24",
        (_, JobStatus.Failed) => "#F87171",
        (_, JobStatus.AbortedBySafetyGuard) => "#F87171",
        _ => "#9CA3AF"
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
