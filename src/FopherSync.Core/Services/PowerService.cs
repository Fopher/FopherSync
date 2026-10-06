using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FopherSync.Core.Services;

/// <summary>
/// Provides Windows Power Management methods (Sleep / Suspend).
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
}
