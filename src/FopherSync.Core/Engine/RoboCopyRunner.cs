using System.Diagnostics;
using System.Text;
using FopherSync.Core.Models;
using FopherSync.Core.Safety;

namespace FopherSync.Core.Engine;

public class RoboCopyRunner
{
    public event Action<string>? OnOutputLine;

    public async Task<JobRunRecord> RunJobAsync(
        BackupJob job,
        string logDirectory,
        bool isDryRun = false,
        CancellationToken cancellationToken = default,
        int defaultRetention = 5)
    {
        var record = new JobRunRecord
        {
            JobId = job.Id,
            JobName = job.Name,
            StartTime = DateTime.UtcNow,
            WasDryRun = isDryRun
        };

        // Ensure log directory exists
        Directory.CreateDirectory(logDirectory);
        var safeJobName = string.Join("_", job.Name.Split(Path.GetInvalidFileNameChars()));
        var logFileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{safeJobName}.log";
        var logFilePath = Path.Combine(logDirectory, logFileName);
        record.LogFilePath = logFilePath;

        // 1. Mandatory Safety Pre-flight validation
        try
        {
            PathSafetyValidator.ValidateJobSafety(job, isDryRun);
        }
        catch (SafetyGuardException ex)
        {
            record.EndTime = DateTime.UtcNow;
            record.DurationSeconds = (record.EndTime - record.StartTime).TotalSeconds;
            record.Status = JobStatus.AbortedBySafetyGuard;
            record.ExitSummary = $"Safety Rail Blocked Execution: [{ex.RuleName}] {ex.Message}";
            record.ErrorDetails = ex.Message;
            OnOutputLine?.Invoke($"[SAFETY RAIL VIOLATION] {ex.Message}");
            return record;
        }

        // 2. Pre-backup script hook
        if (!string.IsNullOrWhiteSpace(job.PreBackupScript))
        {
            OnOutputLine?.Invoke($"[PRE-SCRIPT] Executing pre-backup script: {job.PreBackupScript}");
            var preSuccess = await ExecuteScriptAsync(job.PreBackupScript, cancellationToken);
            if (!preSuccess)
            {
                record.EndTime = DateTime.UtcNow;
                record.DurationSeconds = (record.EndTime - record.StartTime).TotalSeconds;
                record.Status = JobStatus.Failed;
                record.ExitSummary = "Pre-backup script failed or timed out.";
                record.ErrorDetails = "Pre-backup script returned non-zero exit code.";
                return record;
            }
        }

        // 2b. Iterative backups: preserve the previous backup before the main sync touches it
        if (job.KeepIterations && !isDryRun)
        {
            bool archived;
            try
            {
                archived = await BackupArchiver.ArchiveAsync(job, line => OnOutputLine?.Invoke(line), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                record.EndTime = DateTime.UtcNow;
                record.DurationSeconds = (record.EndTime - record.StartTime).TotalSeconds;
                record.Status = JobStatus.Failed;
                record.ExitSummary = "Operation was cancelled by user.";
                OnOutputLine?.Invoke("[CANCELLED] Backup run cancelled.");
                return record;
            }

            if (!archived)
            {
                record.EndTime = DateTime.UtcNow;
                record.DurationSeconds = (record.EndTime - record.StartTime).TotalSeconds;
                record.Status = JobStatus.Failed;
                record.ExitSummary = "Could not archive the previous backup; run aborted to keep the main backup untouched.";
                record.ErrorDetails = "Iterative backup archive step failed (see log output).";
                OnOutputLine?.Invoke("[ARCHIVE] Aborting run: previous backup could not be preserved.");
                return record;
            }
        }

        // 3. Build RoboCopy arguments
        var args = RoboCopyArgsBuilder.BuildArguments(job, logFilePath, isDryRun);
        OnOutputLine?.Invoke($"[START] robocopy.exe {args}");

        var logBuffer = new StringBuilder();
        var startInfo = new ProcessStartInfo
        {
            FileName = "robocopy.exe",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                logBuffer.AppendLine(e.Data);
                OnOutputLine?.Invoke(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                logBuffer.AppendLine($"[STDERR] {e.Data}");
                OnOutputLine?.Invoke($"[STDERR] {e.Data}");
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            record.EndTime = DateTime.UtcNow;
            record.DurationSeconds = (record.EndTime - record.StartTime).TotalSeconds;
            record.ExitCode = process.ExitCode;

            var (status, summary) = ExitCodeEvaluator.Evaluate(process.ExitCode);
            record.Status = status;
            record.ExitSummary = summary;

            // Parse detailed statistics from the captured log output
            var fullLog = logBuffer.ToString();
            var stats = LogParser.ParseSummary(fullLog);
            record.FilesCopied = stats.CopiedFiles;
            record.FilesTotal = stats.TotalFiles;
            record.BytesTransferred = stats.CopiedBytes;
            record.ErrorCount = stats.FailedFiles + stats.FailedDirs;

            OnOutputLine?.Invoke($"[FINISHED] Exit Code: {record.ExitCode} -> {summary}");
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { /* ignore */ }
            record.EndTime = DateTime.UtcNow;
            record.DurationSeconds = (record.EndTime - record.StartTime).TotalSeconds;
            record.Status = JobStatus.Failed;
            record.ExitSummary = "Operation was cancelled by user.";
            OnOutputLine?.Invoke("[CANCELLED] Backup run cancelled.");
            return record;
        }
        catch (Exception ex)
        {
            record.EndTime = DateTime.UtcNow;
            record.DurationSeconds = (record.EndTime - record.StartTime).TotalSeconds;
            record.Status = JobStatus.Failed;
            record.ExitSummary = $"Process error: {ex.Message}";
            record.ErrorDetails = ex.ToString();
            OnOutputLine?.Invoke($"[ERROR] {ex.Message}");
            return record;
        }

        // 3b. Iterative backups: remove the oldest archives beyond the retention count
        if (job.KeepIterations && !isDryRun && record.Status != JobStatus.Failed)
        {
            BackupArchiver.Prune(job, BackupArchiver.ResolveRetention(job, defaultRetention), line => OnOutputLine?.Invoke(line));
        }

        // 4. Post-backup script hook
        if (!string.IsNullOrWhiteSpace(job.PostBackupScript) && record.Status != JobStatus.Failed)
        {
            OnOutputLine?.Invoke($"[POST-SCRIPT] Executing post-backup script: {job.PostBackupScript}");
            await ExecuteScriptAsync(job.PostBackupScript, cancellationToken);
        }

        return record;
    }

    private static async Task<bool> ExecuteScriptAsync(string scriptPath, CancellationToken cancellationToken)
    {
        try
        {
            var isPowerShell = scriptPath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase);
            var psi = new ProcessStartInfo
            {
                FileName = isPowerShell ? "powershell.exe" : "cmd.exe",
                Arguments = isPowerShell ? $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"" : $"/c \"{scriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var proc = Process.Start(psi);
            if (proc == null) return false;
            await proc.WaitForExitAsync(cancellationToken);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
