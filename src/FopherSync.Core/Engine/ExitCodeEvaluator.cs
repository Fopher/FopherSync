using FopherSync.Core.Models;

namespace FopherSync.Core.Engine;

public static class ExitCodeEvaluator
{
    /// <summary>
    /// Interprets RoboCopy's bitmask exit code into a human-readable summary and high-level JobStatus.
    /// </summary>
    public static (JobStatus Status, string Summary) Evaluate(int exitCode)
    {
        // Any exit code >= 16 indicates serious fatal failure
        if (exitCode >= 16)
        {
            return (JobStatus.Failed, $"Fatal Error (Code {exitCode}): RoboCopy encountered an unrecoverable error (e.g. invalid parameters or permission denied).");
        }

        // Any exit code >= 8 indicates copy errors occurred (retry limit exceeded)
        if (exitCode >= 8)
        {
            return (JobStatus.Failed, $"Partial Failure (Code {exitCode}): One or more files could not be copied and retry limits were exceeded.");
        }

        return exitCode switch
        {
            0 => (JobStatus.Success, "Success: Source and Destination are completely in sync (no new files to copy)."),
            1 => (JobStatus.Success, "Success: All new or updated files were copied successfully."),
            2 => (JobStatus.Success, "Success: Extra files exist in destination; no source files needed copying."),
            3 => (JobStatus.Success, "Success: Files were copied successfully, and extra files exist in destination."),
            4 => (JobStatus.Warning, "Warning: Mismatched files or directories detected."),
            5 => (JobStatus.Warning, "Warning: Files copied, but file attribute/security mismatches detected."),
            6 => (JobStatus.Warning, "Warning: Extra files exist and attribute mismatches detected."),
            7 => (JobStatus.Warning, "Warning: Files copied, extra files present, and mismatches detected."),
            _ => (JobStatus.Warning, $"Completed with exit code {exitCode}.")
        };
    }
}
