using System.Text;
using FopherSync.Core.Models;

namespace FopherSync.Core.Engine;

public static class RoboCopyArgsBuilder
{
    public static string BuildArguments(BackupJob job, string? logFilePath = null, bool isDryRun = false)
    {
        var sb = new StringBuilder();

        // Source and Destination paths (quoted to handle spaces, UNC, and drive roots safely)
        sb.Append($"{FormatPathForCli(job.SourcePath)} ");
        sb.Append($"{FormatPathForCli(job.DestinationPath)} ");

        // Copy files filter (all files)
        sb.Append("*.* ");

        // Copy mode flags
        switch (job.Mode)
        {
            case BackupMode.Incremental:
                // /E: Copy subdirectories, including empty ones
                // /XO: Exclude Older files (only overwrite if source file is newer)
                sb.Append("/E /XO ");
                break;

            case BackupMode.Mirror:
                // /MIR: Mirror directory tree (equivalent to /E plus /PURGE)
                sb.Append("/MIR ");
                break;

            case BackupMode.Move:
                // /E: Copy subdirectories
                // /MOVE: Move files and dirs (delete from source after copying)
                sb.Append("/E /MOVE ");
                break;
        }

        // Retry flags (prevent hanging forever on open files)
        var retryCount = Math.Max(0, Math.Min(job.RetryCount, 10));
        var waitSeconds = Math.Max(0, Math.Min(job.RetryWaitSeconds, 60));
        sb.Append($"/R:{retryCount} /W:{waitSeconds} ");

        // Multi-threaded copy (/MT:1 to 128)
        var threads = Math.Max(1, Math.Min(job.ThreadCount, 64));
        sb.Append($"/MT:{threads} ");

        // File exclusions (/XF)
        if (job.ExcludeFiles.Count > 0)
        {
            sb.Append("/XF ");
            foreach (var pattern in job.ExcludeFiles)
            {
                if (!string.IsNullOrWhiteSpace(pattern))
                    sb.Append($"\"{pattern.Trim()}\" ");
            }
        }

        // Directory exclusions (/XD) - always exclude Windows OS system folders on drive roots
        var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "$RECYCLE.BIN",
            "System Volume Information",
            "Recovery"
        };
        foreach (var dir in job.ExcludeDirectories)
        {
            if (!string.IsNullOrWhiteSpace(dir))
                excludedDirs.Add(dir.Trim());
        }

        if (excludedDirs.Count > 0)
        {
            sb.Append("/XD ");
            foreach (var dir in excludedDirs)
            {
                sb.Append($"\"{dir}\" ");
            }
        }

        // Output formatting:
        // /V: Verbose output (shows skipped files)
        // /TS: Include source file Time Stamps
        // /FP: Include Full Pathnames of files in output
        // /NP: No Progress (no % counter spam in log files)
        // /BYTES: Print sizes as bytes for parsing
        sb.Append("/V /TS /FP /NP /BYTES ");

        // If log file specified: Tee output to log file and console (/TEE /LOG+:path)
        if (!string.IsNullOrEmpty(logFilePath))
        {
            sb.Append($"/TEE /LOG+:\"{logFilePath}\" ");
        }

        // Dry run simulation (/L - List only, don't copy, timestamp or delete any files)
        if (isDryRun)
        {
            sb.Append("/L ");
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatPathForCli(string path)
    {
        var trimmed = path.Trim();
        // If it's a drive root like "E:\" or "E:", format as "E:\." to avoid Windows escaped quote bug and specify true root
        if (trimmed.Length == 2 && trimmed[1] == ':')
            return $"\"{trimmed}\\.\"";
        if (trimmed.Length == 3 && trimmed[1] == ':' && (trimmed[2] == '\\' || trimmed[2] == '/'))
            return $"\"{trimmed[0]}:\\.\"";

        // For all other directories, strip trailing slashes to avoid \" escaped quote bug
        var clean = trimmed.TrimEnd('\\', '/');
        return $"\"{clean}\"";
    }
}
