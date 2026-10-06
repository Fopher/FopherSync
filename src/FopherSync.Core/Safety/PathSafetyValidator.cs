using FopherSync.Core.Models;

namespace FopherSync.Core.Safety;

public static class PathSafetyValidator
{
    private static readonly string[] ProtectedSystemFolders =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    ];

    /// <summary>
    /// Validates all paths and parameters before executing a job.
    /// Throws SafetyGuardException if any safety violation is detected.
    /// </summary>
    public static void ValidateJobSafety(BackupJob job, bool isDryRun = false)
    {
        if (string.IsNullOrWhiteSpace(job.SourcePath))
            throw new SafetyGuardException("MissingSource", "Source path cannot be empty.");

        if (string.IsNullOrWhiteSpace(job.DestinationPath))
            throw new SafetyGuardException("MissingDestination", "Destination path cannot be empty.");

        var fullSource = NormalizePath(job.SourcePath);
        var fullDest = NormalizePath(job.DestinationPath);

        // Guard 1: Source and Destination cannot be the exact same path
        if (string.Equals(fullSource, fullDest, StringComparison.OrdinalIgnoreCase))
        {
            throw new SafetyGuardException(
                "IdenticalPaths",
                $"Source and Destination cannot be the same path: '{fullSource}'.",
                fullSource);
        }

        // Guard 2: Destination cannot be a subfolder of Source (infinite loop risk)
        if (IsSubdirectoryOf(fullDest, fullSource))
        {
            throw new SafetyGuardException(
                "DestinationInsideSource",
                $"Destination '{fullDest}' cannot be located inside the Source directory '{fullSource}'. This causes infinite recursive copying.",
                fullDest);
        }

        // Guard 3: Source cannot be a subfolder of Destination in Mirror/Move mode
        if ((job.Mode == BackupMode.Mirror || job.Mode == BackupMode.Move) && IsSubdirectoryOf(fullSource, fullDest))
        {
            throw new SafetyGuardException(
                "SourceInsideDestination",
                $"Source '{fullSource}' cannot be located inside Destination '{fullDest}' during {job.Mode} operations.",
                fullSource);
        }

        // Guard 4: Protect Root Drives (e.g. C:\ or D:\) from being Mirror/Move destinations
        if (job.Mode == BackupMode.Mirror || job.Mode == BackupMode.Move)
        {
            if (IsDriveRoot(fullDest))
            {
                throw new SafetyGuardException(
                    "RootDriveProtection",
                    $"Destination '{fullDest}' is a drive root. Mirroring to a drive root is extremely dangerous and blocked.",
                    fullDest);
            }
        }

        // Guard 5: Protect Windows and System directories
        foreach (var protectedFolder in ProtectedSystemFolders)
        {
            if (string.IsNullOrEmpty(protectedFolder)) continue;

            if (IsSubdirectoryOfOrEqual(fullDest, protectedFolder))
            {
                throw new SafetyGuardException(
                    "ProtectedSystemFolder",
                    $"Destination '{fullDest}' is inside a protected Windows system directory ('{protectedFolder}'). Operation blocked.",
                    fullDest);
            }
        }

        // Guard 6: Source existence check (Must exist on disk or network)
        if (!Directory.Exists(fullSource))
        {
            throw new SafetyGuardException(
                "SourceNotFound",
                $"Source path '{fullSource}' does not exist or is currently unmounted/disconnected.",
                fullSource);
        }

        // Guard 7: Empty Source & Disconnect Guard (CRITICAL for Mirror mode)
        // If a USB drive or NAS share disconnected or appears empty, mirroring would wipe the destination!
        if (job.AbortIfSourceEmpty || job.Mode == BackupMode.Mirror)
        {
            var hasFiles = HasAnyFilesOrDirectories(fullSource);
            if (!hasFiles)
            {
                throw new SafetyGuardException(
                    "EmptySourceProtection",
                    $"Source directory '{fullSource}' is completely empty or inaccessible! The backup was halted to prevent wiping destination files.",
                    fullSource);
            }
        }
    }

    private static string NormalizePath(string path)
    {
        var trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // Allow UNC paths or local rooted paths
        if (trimmed.StartsWith(@"\\"))
            return trimmed;
        return Path.GetFullPath(trimmed);
    }

    private static bool IsDriveRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root)) return false;
        return string.Equals(root.TrimEnd('\\', '/'), path.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSubdirectoryOf(string candidate, string parent)
    {
        var candidateNorm = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var parentNorm = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        return candidateNorm.StartsWith(parentNorm, StringComparison.OrdinalIgnoreCase) &&
               candidateNorm.Length > parentNorm.Length;
    }

    private static bool IsSubdirectoryOfOrEqual(string candidate, string parent)
    {
        var candidateNorm = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parentNorm = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(candidateNorm, parentNorm, StringComparison.OrdinalIgnoreCase))
            return true;

        return IsSubdirectoryOf(candidate, parent);
    }

    private static bool HasAnyFilesOrDirectories(string directoryPath)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(directoryPath).Any();
        }
        catch
        {
            return false;
        }
    }
}
