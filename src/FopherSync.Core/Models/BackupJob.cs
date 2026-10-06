namespace FopherSync.Core.Models;

public class BackupJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Backup Job";
    public string Description { get; set; } = string.Empty;

    // Paths
    public string SourcePath { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;

    // Operational Mode
    public BackupMode Mode { get; set; } = BackupMode.Incremental;

    // Safety and Verification
    public bool AbortIfSourceEmpty { get; set; } = true;
    public bool VerifyHashAfterCopy { get; set; } = false;

    // Iterative backups: preserve the previous backup as "Name_yyyy-MM-dd" before each run
    public bool KeepIterations { get; set; } = false;
    public bool UseGlobalRetention { get; set; } = true;
    public int RetentionCount { get; set; } = 0; // Used when UseGlobalRetention is false

    // Retry settings (prevent stalling forever on open/locked files)
    public int RetryCount { get; set; } = 2;
    public int RetryWaitSeconds { get; set; } = 5;

    // Multi-threaded copying (/MT:n)
    public int ThreadCount { get; set; } = 8;

    // Exclusions
    public List<string> ExcludeFiles { get; set; } = new() { "*.tmp", "Thumbs.db", "desktop.ini" };
    public List<string> ExcludeDirectories { get; set; } = new() { "$RECYCLE.BIN", "System Volume Information", "Recovery", "node_modules" };

    // Scripts
    public string? PreBackupScript { get; set; }
    public string? PostBackupScript { get; set; }

    // Scheduling & Power Management
    public bool IsScheduled { get; set; } = false;
    public ScheduleType ScheduleType { get; set; } = ScheduleType.Daily;
    public TimeSpan ScheduledTime { get; set; } = new TimeSpan(2, 0, 0); // Default 2:00 AM
    public List<DayOfWeek> ScheduledDays { get; set; } = new() { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday };
    public int HourlyInterval { get; set; } = 4;
    public bool WakeToRun { get; set; } = true; // Wake PC from sleep at scheduled time
    public bool CatchupIfMissed { get; set; } = true; // Run ASAP upon wake-up if scheduled time was missed
    public bool SleepAfterComplete { get; set; } = false; // Put PC to sleep when backup finishes

    // Metadata
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastRunAt { get; set; }
    public JobStatus LastRunStatus { get; set; } = JobStatus.Idle;
    public string? LastRunMessage { get; set; }
}

public enum ScheduleType
{
    Daily,
    Weekly,
    Hourly
}
