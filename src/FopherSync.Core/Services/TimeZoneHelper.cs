namespace FopherSync.Core.Services;

public record TimeZoneOption(string Id, string DisplayName, string Region);

public static class TimeZoneHelper
{
    public static string CurrentTimeZoneId { get; set; } = "Local";

    public static readonly IReadOnlyList<TimeZoneOption> SupportedTimeZones = new List<TimeZoneOption>
    {
        // System & Universal
        new("Local", "System Default (Local Machine Time)", "System"),
        new("UTC", "UTC (Coordinated Universal Time)", "Universal"),

        // United States & Territories
        new("Eastern Standard Time", "Eastern Time (US & Canada - ET)", "United States"),
        new("Central Standard Time", "Central Time (US & Canada - CT)", "United States"),
        new("Mountain Standard Time", "Mountain Time (US & Canada - MT)", "United States"),
        new("US Mountain Standard Time", "Arizona (Mountain Time - No DST)", "United States"),
        new("Pacific Standard Time", "Pacific Time (US & Canada - PT)", "United States"),
        new("Alaskan Standard Time", "Alaska Time (AKT)", "United States"),
        new("Hawaiian Standard Time", "Hawaii Time (HST - No DST)", "United States"),

        // Europe
        new("GMT Standard Time", "Western European Time / GMT (London, Dublin, Lisbon)", "Europe"),
        new("W. Europe Standard Time", "Central European Time (Berlin, Paris, Rome, Madrid, Amsterdam - CET)", "Europe"),
        new("Central Europe Standard Time", "Central European Time (Warsaw, Prague, Vienna, Budapest - CET)", "Europe"),
        new("E. Europe Standard Time", "Eastern European Time (Athens, Helsinki, Bucharest, Kyiv - EET)", "Europe")
    };

    /// <summary>
    /// Gets the resolved TimeZoneInfo for the specified ID (or CurrentTimeZoneId / Local).
    /// </summary>
    public static TimeZoneInfo GetTimeZone(string? timeZoneId = null)
    {
        var id = timeZoneId ?? CurrentTimeZoneId;
        if (string.IsNullOrWhiteSpace(id) || id.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch
        {
            return TimeZoneInfo.Local;
        }
    }

    /// <summary>
    /// Converts a DateTime (assumed UTC if Kind is Utc or Unspecified) to the target timezone.
    /// </summary>
    public static DateTime ToTargetTime(DateTime dt, string? timeZoneId = null)
    {
        var targetTz = GetTimeZone(timeZoneId);
        DateTime utc;

        if (dt.Kind == DateTimeKind.Utc)
        {
            utc = dt;
        }
        else if (dt.Kind == DateTimeKind.Local)
        {
            utc = dt.ToUniversalTime();
        }
        else
        {
            // Unspecified is treated as UTC because all RoboCopy runners record UTC timestamps
            utc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeFromUtc(utc, targetTz);
    }

    /// <summary>
    /// Formats a DateTime in the target timezone with standard friendly representation.
    /// </summary>
    public static string FormatDateTime(DateTime dt, string? timeZoneId = null, string format = "MMM dd, yyyy  hh:mm tt")
    {
        var target = ToTargetTime(dt, timeZoneId);
        return target.ToString(format);
    }

    /// <summary>
    /// Returns a short abbreviation or identifier for the target timezone.
    /// </summary>
    public static string GetShortAbbreviation(string? timeZoneId = null)
    {
        var id = timeZoneId ?? CurrentTimeZoneId;
        return id switch
        {
            "Local" => "Local",
            "UTC" => "UTC",
            "Eastern Standard Time" => "ET",
            "Central Standard Time" => "CT",
            "Mountain Standard Time" => "MT",
            "US Mountain Standard Time" => "MST",
            "Pacific Standard Time" => "PT",
            "Alaskan Standard Time" => "AKT",
            "Hawaiian Standard Time" => "HST",
            "GMT Standard Time" => "GMT/WET",
            "W. Europe Standard Time" => "CET",
            "Central Europe Standard Time" => "CET",
            "E. Europe Standard Time" => "EET",
            _ => "Local"
        };
    }

    /// <summary>
    /// Converts the user's scheduled time (which is specified in settings.TimeZoneId)
    /// into local machine time for Windows Task Scheduler registration.
    /// </summary>
    public static TimeSpan GetLocalScheduledTime(string? timeZoneId, TimeSpan scheduledTimeInTz)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            return scheduledTimeInTz;
        }

        try
        {
            var tz = GetTimeZone(timeZoneId);
            var nowInTz = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            var scheduledTodayInTz = new DateTime(nowInTz.Year, nowInTz.Month, nowInTz.Day, scheduledTimeInTz.Hours, scheduledTimeInTz.Minutes, 0);

            var utcTime = TimeZoneInfo.ConvertTimeToUtc(scheduledTodayInTz, tz);
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, TimeZoneInfo.Local);

            return localTime.TimeOfDay;
        }
        catch
        {
            return scheduledTimeInTz;
        }
    }
}
