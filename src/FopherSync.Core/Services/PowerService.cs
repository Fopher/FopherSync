using System.Diagnostics;
using System.Runtime.InteropServices;
using FopherSync.Core.Models;

namespace FopherSync.Core.Services;

/// <summary>
/// Provides Windows Power Management methods (Sleep / Suspend) and quiet-hours evaluation.
/// </summary>
public static class PowerService
{
    [DllImport("Powrprof.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    /// <summary>
    /// Suspends the computer to Sleep mode.
    /// Crucially passes disableWakeEvent = false so hardware wake timers (e.g. Task Scheduler WakeToRun)
    /// remain enabled to wake the machine for the next scheduled backup.
    /// </summary>
    public static bool Sleep()
    {
        try
        {
            Debug.WriteLine("[PowerService] Entering sleep mode...");
            return SetSuspendState(hibernate: false, forceCritical: true, disableWakeEvent: false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PowerService] Failed to set sleep state: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks whether the specified time (or current UTC time) falls within the configured
    /// "Never automatically put to sleep between certain hours" window.
    /// Returns false if PreventSleepBetweenHours is disabled.
    /// </summary>
    public static bool IsWithinPreventSleepHours(AppSettings settings, DateTime? checkTime = null)
    {
        if (settings == null || !settings.PreventSleepBetweenHours)
            return false;

        var tz = TimeZoneHelper.GetTimeZone(settings.TimeZoneId);
        var targetTime = TimeZoneInfo.ConvertTimeFromUtc(checkTime ?? DateTime.UtcNow, tz);
        var current = targetTime.TimeOfDay;

        var start = settings.PreventSleepStart;
        var end = settings.PreventSleepEnd;

        if (start == end)
        {
            // If start and end are identical, treat the whole 24 hours as blocked
            return true;
        }

        if (start < end)
        {
            // Standard daytime window, e.g. 08:00 AM to 06:00 PM
            return current >= start && current < end;
        }
        else
        {
            // Overnight window spanning midnight, e.g. 10:00 PM to 06:00 AM
            return current >= start || current < end;
        }
    }

    /// <summary>
    /// Formats the quiet hours time window as a friendly string (e.g. "08:00 AM – 06:00 PM").
    /// </summary>
    public static string FormatPreventSleepWindow(AppSettings settings)
    {
        if (settings == null) return string.Empty;
        var s = FormatTimeSpan(settings.PreventSleepStart);
        var e = FormatTimeSpan(settings.PreventSleepEnd);
        return $"{s} – {e}";
    }

    /// <summary>
    /// Formats a TimeSpan as a friendly 12-hour string (e.g. "08:00 AM").
    /// </summary>
    public static string FormatTimeSpan(TimeSpan time)
    {
        int h24 = time.Hours;
        int m = time.Minutes;
        string amPm = h24 >= 12 ? "PM" : "AM";
        int h12 = h24 % 12;
        if (h12 == 0) h12 = 12;
        return $"{h12:D2}:{m:D2} {amPm}";
    }
}
