using System.Diagnostics;
using FopherSync.Core.Models;
using FopherSync.Core.Services;

namespace FopherSync.Scheduler;

public class TaskSchedulerService
{
    /// <summary>
    /// The task name visible in Windows Task Scheduler (root folder).
    /// </summary>
    public const string TaskName = "FopherSync";
    private const string LegacyTaskName = "RoboCopyPlus";

    public string? LastError { get; private set; }
    public string? LastOutput { get; private set; }

    /// <summary>
    /// Creates or updates the master daily scheduled task using schtasks.exe.
    /// The task is named "FopherSync" in the root of Task Scheduler.
    /// </summary>
    public bool SyncMasterSchedule(AppSettings settings, string executablePath)
    {
        LastError = null;
        LastOutput = null;

        try
        {
            // Clean up any legacy task if it still exists
            try { RunSchtasks($"/Delete /TN \"{LegacyTaskName}\" /F"); } catch { }
            try { RunSchtasks($"/Delete /TN \"RoboCopyPlus Backup\" /F"); } catch { }

            if (!settings.ScheduleEnabled)
            {
                // Delete the task if schedule is disabled
                DeleteTask();
                return true;
            }

            // Convert scheduled time in user's chosen timezone to local machine time for Windows Task Scheduler
            var localTriggerTime = TimeZoneHelper.GetLocalScheduledTime(settings.TimeZoneId, settings.ScheduledTime);
            var triggerTime = $"{localTriggerTime.Hours:D2}:{localTriggerTime.Minutes:D2}";

            // schtasks /Create with /F to force overwrite if exists
            // /SC DAILY = run every day
            // /TN "FopherSync" = task name in root
            // /TR = the command to run (inner quotes around exe path for spaces)
            // /ST = start time
            // /RL HIGHEST = run with highest privileges

            // Build /TR value: \"C:\path\to\FopherSync.exe\" --run-all-scheduled
            var trInner = "\\\"" + executablePath + "\\\" --run-all-scheduled";

            // Try standard creation first (without /RL HIGHEST so standard users don't get 'Access is denied')
            var args = "/Create /F" +
                       " /TN \"" + TaskName + "\"" +
                       " /SC DAILY" +
                       " /ST " + triggerTime +
                       " /TR \"" + trInner + "\"";

            var (exitCode, output, error) = RunSchtasks(args);

            if (exitCode != 0)
            {
                // Fallback attempt: if 'FopherSync' has an existing orphaned/locked registry key, try 'FopherSync Backup'
                var fallbackArgs = "/Create /F /TN \"FopherSync Backup\" /SC DAILY /ST " + triggerTime + " /TR \"" + trInner + "\"";
                var (fbExitCode, fbOutput, fbError) = RunSchtasks(fallbackArgs);
                if (fbExitCode == 0)
                {
                    LastOutput = fbOutput;
                    ApplyAdvancedSettings(settings, "FopherSync Backup");
                    return true;
                }

                LastError = $"schtasks /Create failed (exit {exitCode}):\n{error}\n{output}";
                return false;
            }

            LastOutput = output;

            // Apply wake-to-run and run-if-missed settings
            ApplyAdvancedSettings(settings, TaskName);

            return true;
        }
        catch (Exception ex)
        {
            LastError = $"Exception: {ex.Message}";
            Debug.WriteLine($"Failed to sync master scheduled task: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Deletes the "FopherSync" task from Task Scheduler (ignores if not found).
    /// </summary>
    public void DeleteTask()
    {
        try
        {
            RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
        }
        catch { }
    }

    /// <summary>
    /// Checks if the task exists in Task Scheduler.
    /// </summary>
    public bool TaskExists()
    {
        try
        {
            var (exitCode, _, _) = RunSchtasks($"/Query /TN \"{TaskName}\"");
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Apply WakeToRun and StartWhenAvailable via PowerShell (schtasks.exe doesn't expose these flags directly).
    /// </summary>
    private void ApplyAdvancedSettings(AppSettings settings, string taskName = TaskName)
    {
        try
        {
            // PowerShell to modify the registered task's settings
            var psScript =
                $"$t = Get-ScheduledTask -TaskName '{taskName}' -ErrorAction Stop; " +
                $"$t.Settings.WakeToRun = ${(settings.WakeToRun ? "true" : "false")}; " +
                $"$t.Settings.StartWhenAvailable = ${(settings.CatchupIfMissed ? "true" : "false")}; " +
                $"$t.Settings.DisallowStartIfOnBatteries = $false; " +
                $"$t.Settings.StopIfGoingOnBatteries = $false; " +
                $"$t.Settings.ExecutionTimeLimit = [System.Xml.XmlConvert]::ToString([TimeSpan]::FromHours(12)); " +
                $"Set-ScheduledTask -InputObject $t | Out-Null";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            proc?.WaitForExit(15000);

            if (proc != null && proc.ExitCode != 0)
            {
                var err = proc.StandardError.ReadToEnd();
                Debug.WriteLine($"PowerShell advanced settings warning: {err}");
                // Non-fatal: the task still exists and will fire, just without wake/catchup
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to apply advanced task settings: {ex.Message}");
            // Non-fatal
        }
    }

    /// <summary>
    /// Runs schtasks.exe with the given arguments and returns (exitCode, stdout, stderr).
    /// </summary>
    private static (int exitCode, string output, string error) RunSchtasks(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return (-1, string.Empty, "Failed to launch schtasks.exe process.");
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(15000);

        return (process.ExitCode, output.Trim(), error.Trim());
    }

    // Legacy cleanup helper — removes old per-job tasks if they exist
    public void CleanupLegacyTasks()
    {
        try
        {
            var (_, output, _) = RunSchtasks("/Query /FO CSV /NH");
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                // CSV format: "\\TaskName","Next Run Time","Status"
                var parts = line.Split(',');
                if (parts.Length > 0)
                {
                    var name = parts[0].Trim('"', '\\', ' ');
                    if (name.StartsWith("RoboCopyPlus_") && name != TaskName)
                    {
                        RunSchtasks($"/Delete /TN \"{name}\" /F");
                    }
                }
            }
        }
        catch { }
    }
}
