using System.Text.RegularExpressions;

namespace FopherSync.Core.Engine;

public class RoboCopyStats
{
    public long TotalDirs { get; set; }
    public long CopiedDirs { get; set; }
    public long FailedDirs { get; set; }

    public long TotalFiles { get; set; }
    public long CopiedFiles { get; set; }
    public long FailedFiles { get; set; }

    public long TotalBytes { get; set; }
    public long CopiedBytes { get; set; }
    public long FailedBytes { get; set; }
}

public static class LogParser
{
    // Regex matches the summary table rows for Dirs, Files, Bytes
    // e.g.: "Files :      1250        20      1230         0         0         5"
    private static readonly Regex RowRegex = new(
        @"^\s*(Dirs|Files|Bytes)\s*:\s*(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public static RoboCopyStats ParseSummary(string fullLog)
    {
        var stats = new RoboCopyStats();
        if (string.IsNullOrWhiteSpace(fullLog)) return stats;

        var matches = RowRegex.Matches(fullLog);
        foreach (Match match in matches)
        {
            var category = match.Groups[1].Value.ToLowerInvariant();
            var totalStr = match.Groups[2].Value;
            var copiedStr = match.Groups[3].Value;
            var failedStr = match.Groups[6].Value;

            switch (category)
            {
                case "dirs":
                    stats.TotalDirs = ParseCount(totalStr);
                    stats.CopiedDirs = ParseCount(copiedStr);
                    stats.FailedDirs = ParseCount(failedStr);
                    break;

                case "files":
                    stats.TotalFiles = ParseCount(totalStr);
                    stats.CopiedFiles = ParseCount(copiedStr);
                    stats.FailedFiles = ParseCount(failedStr);
                    break;

                case "bytes":
                    stats.TotalBytes = ParseCount(totalStr);
                    stats.CopiedBytes = ParseCount(copiedStr);
                    stats.FailedBytes = ParseCount(failedStr);
                    break;
            }
        }

        return stats;
    }

    private static long ParseCount(string value)
    {
        if (long.TryParse(value.Replace(",", ""), out var count))
            return count;
        return 0;
    }
}
