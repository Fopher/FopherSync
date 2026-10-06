using System.Text.Json;
using FopherSync.Core.Models;
using FopherSync.Core.Security;

namespace FopherSync.Core.Services;

public class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string ConfigDirectory { get; }
    public string JobsFilePath => Path.Combine(ConfigDirectory, "jobs.json");
    public string SettingsFilePath => Path.Combine(ConfigDirectory, "settings.json");
    public string DatabaseFilePath => Path.Combine(ConfigDirectory, "history.db");
    public string DefaultLogDirectory => Path.Combine(ConfigDirectory, "logs");

    public ConfigService(string? customBaseDirectory = null)
    {
        if (!string.IsNullOrEmpty(customBaseDirectory))
        {
            ConfigDirectory = customBaseDirectory;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            ConfigDirectory = Path.Combine(appData, "FopherSync");

            // Seamless migration from legacy directory if needed
            var legacyDir = Path.Combine(appData, "RoboCopyPlus");
            if (!Directory.Exists(ConfigDirectory) && Directory.Exists(legacyDir))
            {
                try
                {
                    Directory.CreateDirectory(ConfigDirectory);
                    foreach (var file in Directory.GetFiles(legacyDir))
                    {
                        var dest = Path.Combine(ConfigDirectory, Path.GetFileName(file));
                        if (!File.Exists(dest)) File.Copy(file, dest, true);
                    }
                    var legacyLogs = Path.Combine(legacyDir, "logs");
                    var destLogs = Path.Combine(ConfigDirectory, "logs");
                    if (Directory.Exists(legacyLogs))
                    {
                        Directory.CreateDirectory(destLogs);
                        foreach (var logFile in Directory.GetFiles(legacyLogs))
                        {
                            var dest = Path.Combine(destLogs, Path.GetFileName(logFile));
                            if (!File.Exists(dest)) File.Copy(logFile, dest, true);
                        }
                    }
                }
                catch { /* Ignore migration errors and continue */ }
            }
        }

        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(DefaultLogDirectory);
    }

    public List<BackupJob> LoadJobs()
    {
        try
        {
            if (File.Exists(JobsFilePath))
            {
                var json = File.ReadAllText(JobsFilePath);
                var jobs = JsonSerializer.Deserialize<List<BackupJob>>(json, JsonOptions);
                if (jobs != null) return jobs;
            }
        }
        catch { /* Fallback to empty list or defaults */ }

        // Provide a safe default sample job
        var defaultJobs = new List<BackupJob>
        {
            new()
            {
                Name = "My Documents Backup",
                Description = "Safe incremental backup of Documents",
                SourcePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                DestinationPath = @"D:\Backups\Documents",
                Mode = BackupMode.Incremental,
                AbortIfSourceEmpty = true,
                RetryCount = 2,
                RetryWaitSeconds = 5,
                ThreadCount = 8
            }
        };

        SaveJobs(defaultJobs);
        return defaultJobs;
    }

    public void SaveJobs(List<BackupJob> jobs)
    {
        var json = JsonSerializer.Serialize(jobs, JsonOptions);
        var tempFile = JobsFilePath + ".tmp";
        File.WriteAllText(tempFile, json);
        File.Move(tempFile, JobsFilePath, true);
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
                    // Decrypt DPAPI-encrypted passwords into in-memory plaintext
                    settings.RemoteServer.ServerPassword = DpapiHelper.Unprotect(settings.RemoteServer.ServerPassword);
                    settings.Notifications.SmtpPassword = DpapiHelper.Unprotect(settings.Notifications.SmtpPassword);

                    if (string.IsNullOrEmpty(settings.LogDirectory))
                        settings.LogDirectory = DefaultLogDirectory;
                    return settings;
                }
            }
        }
        catch { /* Fallback */ }

        var defaultSettings = new AppSettings
        {
            LogDirectory = DefaultLogDirectory,
            StartMinimizedToTray = false,
            MinimizeOnClose = true
        };

        SaveSettings(defaultSettings);
        return defaultSettings;
    }

    public void SaveSettings(AppSettings settings)
    {
        // Temporarily encrypt passwords for disk serialization
        var rawServerPass = settings.RemoteServer.ServerPassword;
        var rawSmtpPass = settings.Notifications.SmtpPassword;

        try
        {
            settings.RemoteServer.ServerPassword = DpapiHelper.Protect(rawServerPass);
            settings.Notifications.SmtpPassword = DpapiHelper.Protect(rawSmtpPass);

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tempFile = SettingsFilePath + ".tmp";
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, SettingsFilePath, true);
        }
        finally
        {
            // Restore in-memory plain text passwords immediately
            settings.RemoteServer.ServerPassword = rawServerPass;
            settings.Notifications.SmtpPassword = rawSmtpPass;
        }
    }
}
