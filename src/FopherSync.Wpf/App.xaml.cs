using System.Threading;
using System.Windows;
using FopherSync.Core.Engine;
using FopherSync.Core.Models;
using FopherSync.Core.Services;
using FopherSync.Notifications;

namespace FopherSync.Wpf;

public partial class App : Application
{
    private async void OnAppStartup(object sender, StartupEventArgs e)
    {
        this.DispatcherUnhandledException += (s, args) =>
        {
            try
            {
                var crashPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FopherSync", "crash.log");
                System.IO.File.AppendAllText(crashPath, $"[{DateTime.Now}] {args.Exception}\n\n");
            }
            catch { }
            MessageBox.Show($"FopherSync encountered an unexpected issue:\n{args.Exception.Message}", "FopherSync Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var configService = new ConfigService();
        var settings = configService.LoadSettings();

        // Initialize user's theme mode (Dark, Light, or System)
        ThemeManager.Initialize(settings.ThemeMode);

        // Check if executed silently by Windows Task Scheduler
        if (e.Args.Length >= 1 && (e.Args[0].Equals("--run-all-scheduled", StringComparison.OrdinalIgnoreCase) || e.Args.Contains("--run-all-scheduled", StringComparer.OrdinalIgnoreCase)))
        {
            await RunAllScheduledJobsHeadlessAsync(configService, settings);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0].Equals("--run-job", StringComparison.OrdinalIgnoreCase))
        {
            var jobId = e.Args[1];
            var isSilent = e.Args.Contains("--silent", StringComparer.OrdinalIgnoreCase);

            if (isSilent)
            {
                await RunHeadlessJobAsync(configService, settings, jobId);
                Shutdown();
                return;
            }
        }

        // Single-instance enforcement for GUI launches
        _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance is already running. Signal it to restore its window.
            try
            {
                if (EventWaitHandle.TryOpenExisting(EventName, out var signalEvent))
                {
                    signalEvent.Set();
                    signalEvent.Dispose();
                }
            }
            catch { }

            Shutdown();
            return;
        }

        // Setup background listener for wake-up signals from secondary launches
        try
        {
            _bringToFrontEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            var listenerThread = new Thread(() =>
            {
                while (_bringToFrontEvent != null)
                {
                    try
                    {
                        if (_bringToFrontEvent.WaitOne())
                        {
                            Current?.Dispatcher.Invoke(() =>
                            {
                                if (Current.MainWindow is MainWindow win)
                                {
                                    win.Show();
                                    if (win.WindowState == WindowState.Minimized)
                                        win.WindowState = WindowState.Normal;
                                    win.Activate();
                                    win.Focus();
                                }
                            });
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch { }
                }
            })
            {
                IsBackground = true,
                Name = "SingleInstanceListener"
            };
            listenerThread.Start();
        }
        catch { }

        // Standard GUI launch
        var mainWindow = new MainWindow();
        mainWindow.Show();

        if (settings.StartMinimizedToTray)
        {
            mainWindow.WindowState = WindowState.Minimized;
            mainWindow.Hide();
        }
    }

    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _bringToFrontEvent;
    private const string MutexName = "Local\\FopherSync_SingleInstance_Mutex";
    private const string EventName = "Local\\FopherSync_BringToFront_Event";

    protected override void OnExit(ExitEventArgs e)
    {
        try { _bringToFrontEvent?.Dispose(); } catch { }
        _bringToFrontEvent = null;

        if (_singleInstanceMutex != null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
            try { _singleInstanceMutex.Dispose(); } catch { }
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }

    private static async Task RunHeadlessJobAsync(ConfigService configService, AppSettings settings, string jobId)
    {
        var jobs = configService.LoadJobs();
        var job = jobs.FirstOrDefault(j => j.Id.Equals(jobId, StringComparison.OrdinalIgnoreCase));
        if (job == null) return;

        var runner = new RoboCopyRunner();
        var logDir = string.IsNullOrEmpty(settings.LogDirectory) ? configService.DefaultLogDirectory : settings.LogDirectory;

        // Run RoboCopy job
        var record = await runner.RunJobAsync(job, logDir, false, default, settings.DefaultArchiveRetentionCount);

        // Update Job metadata
        job.LastRunAt = record.EndTime;
        job.LastRunStatus = record.Status;
        job.LastRunMessage = record.ExitSummary;
        configService.SaveJobs(jobs);

        // Record in SQLite history database
        var db = new JobHistoryDatabase(configService.DatabaseFilePath);
        await db.InsertRunRecordAsync(record);

        // Dispatch notifications (ntfy.sh, Discord, Gmail/SMTP)
        var dispatcher = new NotificationDispatcher(settings.Notifications);
        await dispatcher.DispatchAsync(record);

        // If sleep on complete is enabled, display the 60s countdown dialog
        if (settings.SleepAfterComplete)
        {
            var sleepDialog = new SleepCountdownDialog(job.Name);
            sleepDialog.ShowDialog();
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [System.Runtime.InteropServices.DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle, int processInformationClass,
        ref ProcessBasicInformation processInformation, int processInformationLength, out int returnLength);

    /// <summary>
    /// Writes diagnostics describing how/why this scheduled run was launched
    /// (command line, parent process, session, uptime) to help trace unexpected runs.
    /// </summary>
    private static void LogLaunchContext(Action<string> log)
    {
        try
        {
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            log($"[LAUNCH] Command line: {Environment.CommandLine}");
            log($"[LAUNCH] Exe: {Environment.ProcessPath}");
            log($"[LAUNCH] PID {self.Id}, session {self.SessionId}, user {Environment.UserDomainName}\\{Environment.UserName}, interactive={Environment.UserInteractive}");
            log($"[LAUNCH] Process start (local): {self.StartTime:yyyy-MM-dd HH:mm:ss}; system uptime: {TimeSpan.FromMilliseconds(Environment.TickCount64):d\\.hh\\:mm\\:ss}");

            var pbi = new ProcessBasicInformation();
            if (NtQueryInformationProcess(self.Handle, 0, ref pbi, System.Runtime.InteropServices.Marshal.SizeOf<ProcessBasicInformation>(), out _) == 0)
            {
                var parentId = pbi.InheritedFromUniqueProcessId.ToInt32();
                try
                {
                    using var parent = System.Diagnostics.Process.GetProcessById(parentId);
                    string parentPath;
                    try { parentPath = parent.MainModule?.FileName ?? "(unknown path)"; }
                    catch { parentPath = "(path inaccessible)"; }
                    log($"[LAUNCH] Parent process: {parent.ProcessName} (PID {parentId}) {parentPath}");
                }
                catch
                {
                    log($"[LAUNCH] Parent process PID {parentId} (no longer running)");
                }
            }
        }
        catch (Exception ex)
        {
            log($"[LAUNCH] Could not gather launch context: {ex.Message}");
        }
    }

    private static async Task RunAllScheduledJobsHeadlessAsync(ConfigService configService, AppSettings settings)
    {
        var crashLogPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FopherSync", "scheduled_run.log");

        void Log(string msg)
        {
            try { System.IO.File.AppendAllText(crashLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\n"); }
            catch { }
        }

        Log("=== Scheduled run started ===");
        LogLaunchContext(Log);

        var allJobs = configService.LoadJobs();
        Log($"Total jobs loaded: {allJobs.Count}");

        // Prefer jobs explicitly marked for the schedule; fall back to ALL jobs
        var jobsToRun = allJobs.Where(j => j.IsScheduled).ToList();
        if (jobsToRun.Count == 0)
        {
            Log("No jobs explicitly marked IsScheduled — falling back to ALL jobs.");
            jobsToRun = allJobs;
        }

        if (jobsToRun.Count == 0)
        {
            Log("No jobs exist at all. Exiting.");
            return;
        }

        Log($"Running {jobsToRun.Count} job(s): {string.Join(", ", jobsToRun.Select(j => j.Name))}");

        // If Wake-on-LAN is enabled, wake the destination server and await its readiness
        if (settings.RemoteServer.WakeOnLanEnabled && !string.IsNullOrWhiteSpace(settings.RemoteServer.ServerMacAddress))
        {
            Log($"[WoL] Waking destination server '{settings.RemoteServer.ServerHostName}' ({settings.RemoteServer.ServerMacAddress})...");
            await WakeOnLanService.WakeAndAwaitAsync(
                settings.RemoteServer.ServerMacAddress,
                settings.RemoteServer.ServerHostName,
                settings.RemoteServer.WakeWaitSeconds,
                msg => Log(msg));
        }

        var runner = new RoboCopyRunner();
        var logDir = string.IsNullOrEmpty(settings.LogDirectory) ? configService.DefaultLogDirectory : settings.LogDirectory;
        var db = new JobHistoryDatabase(configService.DatabaseFilePath);
        var dispatcher = new NotificationDispatcher(settings.Notifications);

        long totalCopied = 0;
        long totalErrors = 0;

        foreach (var job in jobsToRun)
        {
            Log($"Starting job: '{job.Name}' ({job.Id})");
            var record = await runner.RunJobAsync(job, logDir, false, default, settings.DefaultArchiveRetentionCount);

            totalCopied += record.FilesCopied;
            totalErrors += record.ErrorCount;

            job.LastRunAt = record.EndTime;
            job.LastRunStatus = record.Status;
            job.LastRunMessage = record.ExitSummary;

            await db.InsertRunRecordAsync(record);
            await dispatcher.DispatchAsync(record);

            Log($"Finished job: '{job.Name}' -> {record.Status} ({record.DurationSeconds:F1}s, {record.FilesCopied} copied)");
        }

        // Save updated jobs list with run timestamps
        configService.SaveJobs(allJobs);
        Log("All jobs complete. Saved metadata.");

        // If remote server sleep is enabled, send sleep signal to destination server
        if (settings.RemoteServer.SleepServerAfterComplete)
        {
            Log($"[REMOTE SLEEP] Signaling server '{settings.RemoteServer.ServerHostName}' to sleep...");
            await WakeOnLanService.ExecuteRemoteSleepAsync(
                settings.RemoteServer.ServerHostName,
                settings.RemoteServer.RemoteSleepCommand,
                settings.RemoteServer.ServerUsername,
                settings.RemoteServer.ServerPassword,
                msg => Log(msg));
        }

        // If global sleep setting is enabled, display the 60s countdown dialog before powering down
        if (settings.SleepAfterComplete)
        {
            Log("Sleep-after-complete enabled — showing countdown.");
            var summaryMsg = totalCopied > 0
                ? $"All scheduled backups completed successfully ({totalCopied:N0} files copied)."
                : "All scheduled backups completed. Files are already fully up to date (0 new files needed copying).";

            var sleepDialog = new SleepCountdownDialog("All Scheduled Backups", summaryMsg);
            sleepDialog.ShowDialog();
        }

        Log("=== Scheduled run finished ===");
    }
}
