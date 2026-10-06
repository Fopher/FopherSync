using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FopherSync.Core.Models;

namespace FopherSync.Core.Engine;

/// <summary>
/// Iterative backups: before a run, preserves the existing main backup as a sibling folder
/// named "{MainName}_yyyy-MM-dd" (adds "_HHmm" only if that date already exists),
/// and prunes the oldest archives beyond the retention count.
/// The main backup folder itself is never renamed or restructured.
/// </summary>
public static class BackupArchiver
{
    public static int ResolveRetention(BackupJob job, int globalDefault)
    {
        var n = job.UseGlobalRetention ? globalDefault : job.RetentionCount;
        return Math.Max(0, n);
    }

    private static string TrimPath(string path) =>
        path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>
    /// Returns the main folder name and parent directory, or null if the destination
    /// is a drive/share root (which cannot have sibling archives).
    /// </summary>
    private static (string Name, string Parent)? SplitDestination(string destinationPath)
    {
        var trimmed = TrimPath(destinationPath);
        var name = Path.GetFileName(trimmed);
        var parent = Path.GetDirectoryName(trimmed);
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(parent)) return null;
        return (name, parent);
    }

    public static string ResolveArchivePath(string destinationPath, DateTime oldBackupTime)
    {
        var parts = SplitDestination(destinationPath)
            ?? throw new InvalidOperationException("Destination is a root and cannot be archived.");

        var basePath = Path.Combine(parts.Parent, $"{parts.Name}_{oldBackupTime:yyyy-MM-dd}");
        if (!Directory.Exists(basePath)) return basePath;

        var withMinutes = $"{basePath}_{oldBackupTime:HHmm}";
        if (!Directory.Exists(withMinutes)) return withMinutes;

        var withSeconds = $"{basePath}_{oldBackupTime:HHmmss}";
        if (!Directory.Exists(withSeconds)) return withSeconds;

        for (int i = 2; ; i++)
        {
            var candidate = $"{withSeconds}_{i}";
            if (!Directory.Exists(candidate)) return candidate;
        }
    }

    /// <summary>
    /// Copies the existing main backup into a new archive folder.
    /// Returns true if archived or nothing needed archiving; false on failure.
    /// </summary>
    public static async Task<bool> ArchiveAsync(
        BackupJob job,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var dest = TrimPath(job.DestinationPath);

        if (SplitDestination(dest) == null)
        {
            log?.Invoke("[ARCHIVE] Destination is a drive/share root; iterative backups skipped.");
            return true;
        }

        bool hasContent;
        try
        {
            hasContent = Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any();
        }
        catch (Exception ex)
        {
            log?.Invoke($"[ARCHIVE] Could not inspect destination: {ex.Message}");
            return false;
        }

        if (!hasContent)
        {
            log?.Invoke("[ARCHIVE] No existing backup found; nothing to archive (first run).");
            return true;
        }

        var oldTime = job.LastRunAt?.ToLocalTime() ?? Directory.GetLastWriteTime(dest);
        var archivePath = ResolveArchivePath(dest, oldTime);
        log?.Invoke($"[ARCHIVE] Preserving previous backup -> {archivePath}");

        var psi = new ProcessStartInfo
        {
            FileName = "robocopy.exe",
            Arguments = $"{RoboCopyArgsBuilder.FormatPathForCli(dest)} \"{archivePath}\" *.* /E /COPY:DAT /DCOPY:DAT " +
                        $"/R:{Math.Clamp(job.RetryCount, 0, 10)} /W:{Math.Clamp(job.RetryWaitSeconds, 0, 60)} " +
                        $"/MT:{Math.Clamp(job.ThreadCount, 1, 64)} /NP /NFL /NDL /NJH",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log?.Invoke($"[ARCHIVE] {e.Data.Trim()}"); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log?.Invoke($"[ARCHIVE] [STDERR] {e.Data.Trim()}"); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            // Remove the partial archive so it is never mistaken for a complete one
            TryDelete(archivePath, log);
            throw;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[ARCHIVE] Failed: {ex.Message}");
            TryDelete(archivePath, log);
            return false;
        }

        // Robocopy: exit codes >= 8 indicate failure
        if (process.ExitCode >= 8)
        {
            log?.Invoke($"[ARCHIVE] Archive copy failed (exit code {process.ExitCode}).");
            TryDelete(archivePath, log);
            return false;
        }

        log?.Invoke("[ARCHIVE] Previous backup preserved.");
        return true;
    }

    /// <summary>Deletes the oldest archive folders beyond <paramref name="keepCount"/>.</summary>
    public static void Prune(BackupJob job, int keepCount, Action<string>? log)
    {
        var parts = SplitDestination(job.DestinationPath);
        if (parts == null || !Directory.Exists(parts.Value.Parent)) return;

        // Strict match so unrelated folders are never touched
        var regex = new Regex(
            $@"^{Regex.Escape(parts.Value.Name)}_(\d{{4}}-\d{{2}}-\d{{2}})(_\d{{4,6}}(_\d+)?)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var archives = new List<(string Path, DateTime Date, string Suffix)>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(parts.Value.Parent))
            {
                var m = regex.Match(Path.GetFileName(dir));
                if (!m.Success) continue;
                if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
                archives.Add((dir, date, m.Groups[2].Value));
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"[ARCHIVE] Could not scan for old archives: {ex.Message}");
            return;
        }

        // Newest first: by date, then by suffix (time-stamped same-day archives sort after the plain one's date)
        var ordered = archives
            .OrderByDescending(a => a.Date)
            .ThenByDescending(a => a.Suffix, StringComparer.Ordinal)
            .ToList();

        foreach (var old in ordered.Skip(keepCount))
        {
            log?.Invoke($"[ARCHIVE] Removing old archive (keeping {keepCount}): {old.Path}");
            TryDelete(old.Path, log);
        }
    }

    private static void TryDelete(string path, Action<string>? log)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            }
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            log?.Invoke($"[ARCHIVE] Could not delete '{path}': {ex.Message}");
        }
    }
}
