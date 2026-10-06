namespace FopherSync.Core.Models;

public class AppSettings
{
    public bool StartMinimizedToTray { get; set; } = false;
    public bool MinimizeOnClose { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public string LogDirectory { get; set; } = string.Empty;
    public string ThemeMode { get; set; } = "Dark"; // "Dark", "Light", or "System"

    // Iterative backups: global default number of older backups to keep (0 = keep none / disabled by default)
    public int DefaultArchiveRetentionCount { get; set; } = 0;

    // Global Master Schedule & Power Management
    public string TimeZoneId { get; set; } = "Local";
    public bool ScheduleEnabled { get; set; } = true;
    public TimeSpan ScheduledTime { get; set; } = new TimeSpan(2, 0, 0); // 02:00 AM
    public bool WakeToRun { get; set; } = true; // Wake PC from sleep at scheduled time
    public bool CatchupIfMissed { get; set; } = true; // Run immediately upon wake-up if missed
    public bool SleepAfterComplete { get; set; } = false; // Put PC to sleep when all jobs finish

    // Remote Server Power (Wake-on-LAN & Remote Sleep)
    public RemoteServerSettings RemoteServer { get; set; } = new();

    public NotificationSettings Notifications { get; set; } = new();
}

public class RemoteServerSettings
{
    public bool WakeOnLanEnabled { get; set; } = false;
    public string ServerMacAddress { get; set; } = string.Empty; // e.g. "00:11:22:33:44:55"
    public string ServerHostName { get; set; } = "FMC-SERVER"; // IP or hostname
    public int WakeWaitSeconds { get; set; } = 90; // Max seconds to wait for server to come online

    public bool SleepServerAfterComplete { get; set; } = false;
    public string RemoteSleepCommand { get; set; } = string.Empty; // Optional custom sleep command (e.g. psshutdown.exe \\FMC-SERVER -d -t 0)

    // Optional Remote Credentials (for authenticated WinRM in home Workgroups)
    public string ServerUsername { get; set; } = string.Empty;
    public string ServerPassword { get; set; } = string.Empty;
}

public class NotificationSettings
{
    // Master Switches
    public bool NotifyOnSuccess { get; set; } = true;
    public bool NotifyOnWarning { get; set; } = true;
    public bool NotifyOnFailure { get; set; } = true;

    // ntfy.sh (Primary Push to Phone)
    public bool NtfyEnabled { get; set; } = false;
    public string NtfyServerUrl { get; set; } = "https://ntfy.sh";
    public string NtfyTopic { get; set; } = ""; // e.g. "my-secret-backup-alert-9821"

    // Discord Webhook (Secondary Push)
    public bool DiscordEnabled { get; set; } = false;
    public string DiscordWebhookUrl { get; set; } = "";

    // Gmail / SMTP Email
    public bool SmtpEnabled { get; set; } = false;
    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;
    public string SmtpUsername { get; set; } = ""; // Gmail address
    public string SmtpPassword { get; set; } = ""; // Gmail App Password
    public string SenderEmail { get; set; } = "";
    public string RecipientEmail { get; set; } = "";
}
