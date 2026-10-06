using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using FopherSync.Core.Engine;
using FopherSync.Core.Models;
using FopherSync.Core.Services;
using FopherSync.Notifications;
using FopherSync.Scheduler;

namespace FopherSync.Wpf;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService;
    private readonly TaskSchedulerService _schedulerService;
    private readonly JobHistoryDatabase _historyDb;
    private readonly ConcurrentQueue<string> _logBuffer = new();
    private readonly DispatcherTimer _logFlushTimer = new();
    private CancellationTokenSource? _activeJobCts;
    private List<BackupJob> _jobs = new();
    private AppSettings _settings;
    private BackupJob? _currentEditingJob;
    private bool _isExplicitExit = false;

    public MainWindow()
    {
        InitializeComponent();

        _configService = new ConfigService();
        _schedulerService = new TaskSchedulerService();
        _settings = _configService.LoadSettings();
        _historyDb = new JobHistoryDatabase(_configService.DatabaseFilePath);

        _logFlushTimer.Interval = TimeSpan.FromMilliseconds(75);
        _logFlushTimer.Tick += OnLogFlushTick;
        _logFlushTimer.Start();

        InitializeAppIcon();
        LoadInitialData();
    }

    private void LoadInitialData()
    {
        _jobs = _configService.LoadJobs();
        TimeZoneHelper.CurrentTimeZoneId = _settings.TimeZoneId;
        RefreshDashboard();
        PopulateSettingsForm();

        // Sync master task in Windows Task Scheduler
        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
        if (!string.IsNullOrEmpty(exePath))
        {
            var success = _schedulerService.SyncMasterSchedule(_settings, exePath);
            if (!success && _settings.ScheduleEnabled)
            {
                TxtSidebarSchedulerStatus.Text = "⚠️ Schedule Unsynced";
                if (FindResource("WarningAmberBrush") is System.Windows.Media.Brush amber)
                    DotSchedulerStatus.Fill = amber;
            }
        }
    }

    private void RefreshDashboard()
    {
        DashboardJobsList.ItemsSource = null;
        DashboardJobsList.ItemsSource = _jobs;
        TxtTotalJobsCount.Text = _jobs.Count.ToString();

        var lastRunJob = _jobs.OrderByDescending(j => j.LastRunAt).FirstOrDefault(j => j.LastRunAt.HasValue);
        if (lastRunJob != null && lastRunJob.LastRunAt.HasValue)
        {
            var formatted = TimeZoneHelper.FormatDateTime(lastRunJob.LastRunAt.Value, _settings.TimeZoneId, "MMM dd, h:mm tt");
            var tzAbbrev = TimeZoneHelper.GetShortAbbreviation(_settings.TimeZoneId);
            TxtLastRunStatus.Text = $"{lastRunJob.LastRunStatus} ({formatted} {tzAbbrev})";
        }

        // Calculate real upcoming schedule from global settings
        var scheduledJobsCount = _jobs.Count(j => j.IsScheduled);
        if (_settings.ScheduleEnabled && scheduledJobsCount > 0)
        {
            var tz = TimeZoneHelper.GetTimeZone(_settings.TimeZoneId);
            var nowInTz = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            var targetInTz = nowInTz.Date.Add(_settings.ScheduledTime);
            if (targetInTz <= nowInTz) targetInTz = targetInTz.AddDays(1);
            var isToday = targetInTz.Date == nowInTz.Date;
            var dayStr = isToday ? "Today" : "Tomorrow";
            var tzAbbrev = TimeZoneHelper.GetShortAbbreviation(_settings.TimeZoneId);

            TxtNextRunStatus.Text = $"{dayStr} @ {targetInTz:hh:mm tt} {tzAbbrev}";
            TxtNextRunSubtext.Text = $"{scheduledJobsCount} {(scheduledJobsCount == 1 ? "job" : "jobs")} included (Catch-up active)";

            TxtSidebarSchedulerStatus.Text = $"Daily @ {targetInTz:hh:mm tt} {tzAbbrev}";
            if (FindResource("SuccessGreenBrush") is System.Windows.Media.Brush green)
                DotSchedulerStatus.Fill = green;
        }
        else if (_settings.ScheduleEnabled && scheduledJobsCount == 0)
        {
            TxtNextRunStatus.Text = "No Jobs Included";
            TxtNextRunSubtext.Text = "Mark jobs as 'Included' in Job Editor";
            TxtSidebarSchedulerStatus.Text = "0 Jobs Included";
            if (FindResource("WarningAmberBrush") is System.Windows.Media.Brush amber)
                DotSchedulerStatus.Fill = amber;
        }
        else
        {
            TxtNextRunStatus.Text = "Disabled";
            TxtNextRunSubtext.Text = "Enable in Settings & Alerts";
            TxtSidebarSchedulerStatus.Text = "Scheduler Disabled";
            if (FindResource("TextSecondaryBrush") is System.Windows.Media.Brush gray)
                DotSchedulerStatus.Fill = gray;
        }
    }

    private void OnOpenTaskSchedulerClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskschd.msc") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not launch Windows Task Scheduler: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #region Navigation

    private void OnNavChanged(object sender, RoutedEventArgs e)
    {
        if (DashboardView == null || JobEditorView == null || HistoryView == null || SettingsView == null) return;

        DashboardView.Visibility = Visibility.Collapsed;
        JobEditorView.Visibility = Visibility.Collapsed;
        HistoryView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;

        if (NavDashboardRadio.IsChecked == true)
        {
            DashboardView.Visibility = Visibility.Visible;
            RefreshDashboard();
        }
        else if (NavHistoryRadio.IsChecked == true)
        {
            HistoryView.Visibility = Visibility.Visible;
            InitHistoryFiltersAndLoad();
        }
        else if (NavSettingsRadio.IsChecked == true)
        {
            SettingsView.Visibility = Visibility.Visible;
            PopulateSettingsForm();
        }
    }

    #endregion

    #region Job Actions & Execution

    private async void OnRunJobNowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string jobId)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job != null)
            {
                // Double confirmation if destructive mode
                if (job.Mode == BackupMode.Mirror || job.Mode == BackupMode.Move)
                {
                    var confirm = MessageBox.Show(
                        $"Warning: Job '{job.Name}' is set to {job.Mode} mode.\n\n" +
                        "Files that exist in destination but not in source may be deleted or modified.\n\n" +
                        "Are you sure you want to proceed with this backup?",
                        "Confirm Destructive Operation",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (confirm != MessageBoxResult.Yes) return;
                }

                // If destination or source is on a network server, ensure the server is awake first
                var host = string.IsNullOrWhiteSpace(_settings.RemoteServer.ServerHostName) ? "FMC-SERVER" : _settings.RemoteServer.ServerHostName;
                var hasMac = !string.IsNullOrWhiteSpace(_settings.RemoteServer.ServerMacAddress);
                var isNetworkJob = job.DestinationPath.StartsWith(@"\\") || job.SourcePath.StartsWith(@"\\") ||
                                   job.DestinationPath.Contains(host, StringComparison.OrdinalIgnoreCase) || job.SourcePath.Contains(host, StringComparison.OrdinalIgnoreCase);

                if (isNetworkJob && hasMac)
                {
                    NavHistoryRadio.IsChecked = true;
                    if (TabHistoryConsoleRadio != null) TabHistoryConsoleRadio.IsChecked = true;

                    var isOnline = await WakeOnLanService.PingHostAsync(host, 1500);
                    if (!isOnline)
                    {
                        AppendConsoleLine($"[WoL] Destination server '{host}' is sleeping. Sending Wake-on-LAN magic packet ({_settings.RemoteServer.ServerMacAddress})...");
                        TxtSidebarSchedulerStatus.Text = $"Waking {host}...";
                        await WakeOnLanService.WakeAndAwaitAsync(
                            _settings.RemoteServer.ServerMacAddress,
                            host,
                            _settings.RemoteServer.WakeWaitSeconds > 0 ? _settings.RemoteServer.WakeWaitSeconds : 90,
                            msg => AppendConsoleLine(msg));
                    }
                }

                await ExecuteJobAsync(job, isDryRun: false);
            }
        }
    }

    private async void OnSimulateJobClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string jobId)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job != null)
            {
                await ExecuteJobAsync(job, isDryRun: true);
            }
        }
    }

    private async void OnRunAllJobsClick(object sender, RoutedEventArgs e)
    {
        if (_activeJobCts != null)
        {
            MessageBox.Show("A backup is currently running. Please wait for it to complete or click 'Stop Backup'.", "Backup in Progress", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var scheduledJobs = _jobs.Where(j => j.IsScheduled).ToList();
        if (scheduledJobs.Count == 0) scheduledJobs = _jobs;

        if (scheduledJobs.Count == 0)
        {
            MessageBox.Show("No backup jobs exist. Click '+ Create New Job' to set one up.", "No Jobs", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Switch to history/console view immediately so user sees live progress
        NavHistoryRadio.IsChecked = true;
        if (TabHistoryConsoleRadio != null) TabHistoryConsoleRadio.IsChecked = true;

        var host = string.IsNullOrWhiteSpace(_settings.RemoteServer.ServerHostName) ? "FMC-SERVER" : _settings.RemoteServer.ServerHostName;
        var hasMac = !string.IsNullOrWhiteSpace(_settings.RemoteServer.ServerMacAddress);

        if (hasMac)
        {
            TxtSidebarSchedulerStatus.Text = $"Waking {host}...";
            AppendConsoleLine($"[WoL] Sending Wake-on-LAN to {host} ({_settings.RemoteServer.ServerMacAddress})...");
            await WakeOnLanService.WakeAndAwaitAsync(
                _settings.RemoteServer.ServerMacAddress,
                host,
                _settings.RemoteServer.WakeWaitSeconds > 0 ? _settings.RemoteServer.WakeWaitSeconds : 90,
                msg => AppendConsoleLine(msg));
        }

        foreach (var job in scheduledJobs)
        {
            await ExecuteJobAsync(job, isDryRun: false);
        }

        if (_settings.RemoteServer.SleepServerAfterComplete)
        {
            AppendConsoleLine($"[REMOTE SLEEP] Putting destination server '{host}' to sleep...");
            await WakeOnLanService.ExecuteRemoteSleepAsync(
                host,
                _settings.RemoteServer.RemoteSleepCommand,
                _settings.RemoteServer.ServerUsername,
                _settings.RemoteServer.ServerPassword,
                msg => AppendConsoleLine(msg));
        }

        if (_settings.SleepAfterComplete)
        {
            var sleepDialog = new SleepCountdownDialog("All Scheduled Backups");
            sleepDialog.Show();
        }
    }

    private async Task ExecuteJobAsync(BackupJob job, bool isDryRun)
    {
        if (_activeJobCts != null)
        {
            MessageBox.Show("A backup job is currently in progress. Please wait for it to complete or click 'Stop Backup'.", "Backup in Progress", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _activeJobCts = new CancellationTokenSource();

        // Switch to history/logs view so user sees live output
        NavHistoryRadio.IsChecked = true;
        if (TabHistoryConsoleRadio != null) TabHistoryConsoleRadio.IsChecked = true;

        // Show running indicators
        TxtHistorySubtitle.Text = $"Running: '{job.Name}' {(isDryRun ? "[Simulating...]" : "[Backing up...]")}";
        PrgRunningStatus.Visibility = Visibility.Visible;
        BtnCancelJob.Visibility = Visibility.Visible;
        TxtSidebarSchedulerStatus.Text = $"Running: {job.Name}";

        AppendConsoleLine($"========== Starting Backup Job: '{job.Name}' {(isDryRun ? "[SIMULATION / WHAT-IF]" : "")} ==========");

        var runner = new RoboCopyRunner();
        runner.OnOutputLine += line => _logBuffer.Enqueue(line);

        var logDir = string.IsNullOrEmpty(_settings.LogDirectory) ? _configService.DefaultLogDirectory : _settings.LogDirectory;
        JobRunRecord record;

        try
        {
            record = await runner.RunJobAsync(job, logDir, isDryRun, _activeJobCts.Token, _settings.DefaultArchiveRetentionCount);
        }
        finally
        {
            _activeJobCts?.Dispose();
            _activeJobCts = null;

            // Reset running indicators
            TxtHistorySubtitle.Text = "Live console output and persistent historical run audit.";
            PrgRunningStatus.Visibility = Visibility.Collapsed;
            BtnCancelJob.Visibility = Visibility.Collapsed;
        }

        AppendConsoleLine($"========== Job Finished with Status: {record.Status} (Duration: {record.DurationSeconds:F1}s) ==========\n");

        if (!isDryRun)
        {
            job.LastRunAt = record.EndTime;
            job.LastRunStatus = record.Status;
            job.LastRunMessage = record.ExitSummary;
            _configService.SaveJobs(_jobs);

            await _historyDb.InsertRunRecordAsync(record);

            var dispatcher = new NotificationDispatcher(_settings.Notifications);
            await dispatcher.DispatchAsync(record);
        }

        RefreshDashboard();
        _ = LoadHistoryRunsAsync();

        if (!isDryRun && job.SleepAfterComplete)
        {
            var sleepDialog = new SleepCountdownDialog(job.Name);
            sleepDialog.Show();
        }
    }

    private void OnCancelJobClick(object sender, RoutedEventArgs e)
    {
        if (_activeJobCts != null)
        {
            _activeJobCts.Cancel();
            AppendConsoleLine("[USER CANCEL] Stopping backup process...");
        }
    }

    private void OnLogFlushTick(object? sender, EventArgs e)
    {
        if (_logBuffer.IsEmpty) return;

        var sb = new System.Text.StringBuilder();
        int count = 0;
        while (_logBuffer.TryDequeue(out var line) && count < 100)
        {
            sb.Append($"[{DateTime.Now:HH:mm:ss}] {line}\n");
            count++;
        }

        if (sb.Length > 0)
        {
            TxtConsoleLog.AppendText(sb.ToString());
            if (TxtConsoleLog.Text.Length > 300_000)
            {
                TxtConsoleLog.Text = TxtConsoleLog.Text.Substring(100_000);
            }
            TxtConsoleLog.ScrollToEnd();
        }
    }

    private void AppendConsoleLine(string line)
    {
        _logBuffer.Enqueue(line);
    }

    private void OnClearConsoleClick(object sender, RoutedEventArgs e)
    {
        TxtConsoleLog.Clear();
    }

    #endregion

    #region History & Logs

    private bool _historyJobFilterInitialized = false;

    private void OnHistorySubTabChanged(object sender, RoutedEventArgs e)
    {
        if (PanelHistoryAudit == null || PanelHistoryConsole == null) return;

        if (TabHistoryAuditRadio.IsChecked == true)
        {
            PanelHistoryAudit.Visibility = Visibility.Visible;
            PanelHistoryConsole.Visibility = Visibility.Collapsed;
            _ = LoadHistoryRunsAsync();
        }
        else if (TabHistoryConsoleRadio.IsChecked == true)
        {
            PanelHistoryAudit.Visibility = Visibility.Collapsed;
            PanelHistoryConsole.Visibility = Visibility.Visible;
        }
    }

    private void InitHistoryFiltersAndLoad()
    {
        if (!_historyJobFilterInitialized && CmbHistoryJobFilter != null)
        {
            CmbHistoryJobFilter.Items.Clear();
            CmbHistoryJobFilter.Items.Add(new ComboBoxItem { Content = "All Configured Jobs", Tag = null, IsSelected = true });
            foreach (var job in _jobs)
            {
                CmbHistoryJobFilter.Items.Add(new ComboBoxItem { Content = job.Name, Tag = job.Id });
            }
            _historyJobFilterInitialized = true;
        }

        _ = LoadHistoryRunsAsync();
    }

    private void OnHistoryFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _ = LoadHistoryRunsAsync();
    }

    private void OnRefreshHistoryClick(object sender, RoutedEventArgs e)
    {
        _ = LoadHistoryRunsAsync();
    }

    private async Task LoadHistoryRunsAsync()
    {
        if (HistoryRunsList == null) return;

        try
        {
            DateTime? since = null;
            if (CmbHistoryTimeFilter != null)
            {
                switch (CmbHistoryTimeFilter.SelectedIndex)
                {
                    case 0: // Last 7 Days (Default)
                        since = DateTime.Now.AddDays(-7);
                        break;
                    case 1: // Last 24 Hours
                        since = DateTime.Now.AddHours(-24);
                        break;
                    case 2: // Last 30 Days
                        since = DateTime.Now.AddDays(-30);
                        break;
                    case 3: // All Time
                        since = null;
                        break;
                    default:
                        since = DateTime.Now.AddDays(-7);
                        break;
                }
            }

            string? jobId = null;
            if (CmbHistoryJobFilter?.SelectedItem is ComboBoxItem selItem && selItem.Tag is string id)
            {
                jobId = id;
            }

            var records = await _historyDb.GetRunsFilteredAsync(since, jobId, limit: 300);

            HistoryRunsList.ItemsSource = null;
            HistoryRunsList.ItemsSource = records;

            if (BdrHistoryEmptyState != null)
                BdrHistoryEmptyState.Visibility = records.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            HistoryRunsList.Visibility = records.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

            if (TxtHistoryCount != null)
                TxtHistoryCount.Text = $"{records.Count} {(records.Count == 1 ? "run" : "runs")} recorded";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load history runs: {ex.Message}");
        }
    }

    private void OnViewRunLogClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string logPath && !string.IsNullOrWhiteSpace(logPath))
        {
            if (File.Exists(logPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open log file:\n{ex.Message}", "Log Open Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show($"Log file not found on disk at:\n{logPath}", "Log File Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        else
        {
            MessageBox.Show("No log file was recorded for this run.", "No Log Available", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnOpenLogsFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var logDir = string.IsNullOrEmpty(_settings.LogDirectory) ? _configService.DefaultLogDirectory : _settings.LogDirectory;
            if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
            Process.Start(new ProcessStartInfo("explorer.exe", logDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open logs folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Job Editor Form

    private void ShowJobEditor(string title, BackupJob job, bool isNew)
    {
        _currentEditingJob = job;
        PopulateJobEditor(job);
        TxtEditorTitle.Text = title;
        if (BtnDeleteJob != null)
        {
            BtnDeleteJob.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;
        }

        DashboardView.Visibility = Visibility.Collapsed;
        HistoryView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        JobEditorView.Visibility = Visibility.Visible;

        // Deselect sidebar radios while inside the editor view
        NavDashboardRadio.IsChecked = false;
        NavHistoryRadio.IsChecked = false;
        NavSettingsRadio.IsChecked = false;
    }

    private void ReturnToDashboard()
    {
        JobEditorView.Visibility = Visibility.Collapsed;
        HistoryView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        DashboardView.Visibility = Visibility.Visible;

        if (NavDashboardRadio.IsChecked != true)
        {
            NavDashboardRadio.IsChecked = true;
        }
        else
        {
            RefreshDashboard();
        }
    }

    private void OnCreateNewJobClick(object sender, RoutedEventArgs e)
    {
        ShowJobEditor("Create Backup Job", new BackupJob(), isNew: true);
    }

    private void OnEditJobClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string jobId)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job != null)
            {
                ShowJobEditor($"Edit Job: {job.Name}", job, isNew: false);
            }
        }
    }

    private void PopulateJobEditor(BackupJob job)
    {
        EdtJobName.Text = job.Name;
        EdtSourcePath.Text = job.SourcePath;
        EdtDestinationPath.Text = job.DestinationPath;
        CmbBackupMode.SelectedIndex = (int)job.Mode;
        ChkAbortIfSourceEmpty.IsChecked = job.AbortIfSourceEmpty;
        ChkEnableSchedule.IsChecked = job.IsScheduled;
        ChkKeepIterations.IsChecked = job.KeepIterations;
        RdoUseGlobalRetention.IsChecked = job.UseGlobalRetention;
        RdoCustomRetention.IsChecked = !job.UseGlobalRetention;
        EdtJobRetentionCount.Text = Math.Max(0, job.RetentionCount).ToString();
        PanelIterationOptions.Visibility = job.KeepIterations ? Visibility.Visible : Visibility.Collapsed;
        BdrDestructiveWarning.Visibility = (job.Mode == BackupMode.Mirror || job.Mode == BackupMode.Move) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnKeepIterationsChanged(object sender, RoutedEventArgs e)
    {
        if (PanelIterationOptions == null) return;
        PanelIterationOptions.Visibility = ChkKeepIterations.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBackupModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BdrDestructiveWarning == null) return;
        var isDestructive = CmbBackupMode.SelectedIndex > 0;
        BdrDestructiveWarning.Visibility = isDestructive ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBrowseSourceClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select Source Folder to Back Up" };
        if (dialog.ShowDialog() == true)
        {
            EdtSourcePath.Text = dialog.FolderName;
        }
    }

    private void OnBrowseDestinationClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select Backup Destination Folder" };
        if (dialog.ShowDialog() == true)
        {
            EdtDestinationPath.Text = dialog.FolderName;
        }
    }

    private void OnSaveJobClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(EdtJobName.Text))
        {
            MessageBox.Show("Please enter a job name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(EdtSourcePath.Text) || string.IsNullOrWhiteSpace(EdtDestinationPath.Text))
        {
            MessageBox.Show("Please specify both Source and Destination folders.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_currentEditingJob == null)
            _currentEditingJob = new BackupJob();

        _currentEditingJob.Name = EdtJobName.Text.Trim();
        _currentEditingJob.SourcePath = EdtSourcePath.Text.Trim();
        _currentEditingJob.DestinationPath = EdtDestinationPath.Text.Trim();
        _currentEditingJob.Mode = (BackupMode)CmbBackupMode.SelectedIndex;
        _currentEditingJob.AbortIfSourceEmpty = ChkAbortIfSourceEmpty.IsChecked == true;
        _currentEditingJob.IsScheduled = ChkEnableSchedule.IsChecked == true;
        _currentEditingJob.KeepIterations = ChkKeepIterations.IsChecked == true;
        _currentEditingJob.UseGlobalRetention = RdoUseGlobalRetention.IsChecked == true;
        if (int.TryParse(EdtJobRetentionCount.Text.Trim(), out var jobRetention))
            _currentEditingJob.RetentionCount = Math.Clamp(jobRetention, 0, 999);

        if (!_jobs.Any(j => j.Id == _currentEditingJob.Id))
            _jobs.Add(_currentEditingJob);

        _configService.SaveJobs(_jobs);

        // Sync Windows Task Scheduler master task
        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
        if (!string.IsNullOrEmpty(exePath))
        {
            var syncOk = _schedulerService.SyncMasterSchedule(_settings, exePath);
            if (!syncOk && _settings.ScheduleEnabled)
            {
                MessageBox.Show(
                    $"Job saved, but the scheduled task could not be updated in Windows Task Scheduler.\n\n" +
                    $"Error: {_schedulerService.LastError}\n\n" +
                    "Try running FopherSync as Administrator once.",
                    "Task Scheduler Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        _historyJobFilterInitialized = false;
        MessageBox.Show("Backup job saved successfully!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        ReturnToDashboard();
    }

    private void OnDeleteJobClick(object sender, RoutedEventArgs e)
    {
        if (_currentEditingJob != null)
        {
            var confirm = MessageBox.Show($"Delete job '{_currentEditingJob.Name}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes)
            {
                _jobs.RemoveAll(j => j.Id == _currentEditingJob.Id);
                _configService.SaveJobs(_jobs);

                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(exePath))
                {
                    _schedulerService.SyncMasterSchedule(_settings, exePath);
                }

                _historyJobFilterInitialized = false;
                ReturnToDashboard();
            }
        }
    }

    private void OnCancelEditClick(object sender, RoutedEventArgs e)
    {
        ReturnToDashboard();
    }

    #endregion

    #region Settings & Alerts

    private void OnSettingsSubTabChanged(object sender, RoutedEventArgs e)
    {
        if (PanelSettingsSchedule == null || PanelSettingsAlerts == null || PanelSettingsGeneral == null) return;

        PanelSettingsSchedule.Visibility = Visibility.Collapsed;
        PanelSettingsAlerts.Visibility = Visibility.Collapsed;
        PanelSettingsGeneral.Visibility = Visibility.Collapsed;

        if (TabSettingsScheduleRadio.IsChecked == true)
        {
            PanelSettingsSchedule.Visibility = Visibility.Visible;
        }
        else if (TabSettingsAlertsRadio.IsChecked == true)
        {
            PanelSettingsAlerts.Visibility = Visibility.Visible;
        }
        else if (TabSettingsGeneralRadio.IsChecked == true)
        {
            PanelSettingsGeneral.Visibility = Visibility.Visible;
        }
    }

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dataDir = _configService.ConfigDirectory;
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
            Process.Start(new ProcessStartInfo("explorer.exe", dataDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open data folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void InitScheduleTimeControls()
    {
        if (CmbSchedMinute != null && CmbSchedMinute.Items.Count == 0)
        {
            for (int m = 0; m < 60; m++)
            {
                CmbSchedMinute.Items.Add(new ComboBoxItem { Content = m.ToString("D2") });
            }
        }
    }

    private void InitTimeZoneControls()
    {
        if (CmbTimeZone == null || CmbTimeZone.Items.Count > 0) return;

        foreach (var tz in TimeZoneHelper.SupportedTimeZones)
        {
            CmbTimeZone.Items.Add(new ComboBoxItem
            {
                Content = $"{tz.DisplayName}",
                Tag = tz.Id
            });
        }
    }

    private void PopulateSettingsForm()
    {
        InitScheduleTimeControls();
        InitTimeZoneControls();
        ChkGlobalScheduleEnabled.IsChecked = _settings.ScheduleEnabled;

        if (CmbTimeZone != null)
        {
            foreach (ComboBoxItem item in CmbTimeZone.Items)
            {
                if (item.Tag is string id && id.Equals(_settings.TimeZoneId, StringComparison.OrdinalIgnoreCase))
                {
                    CmbTimeZone.SelectedItem = item;
                    break;
                }
            }
            if (CmbTimeZone.SelectedIndex == -1 && CmbTimeZone.Items.Count > 0)
                CmbTimeZone.SelectedIndex = 0;
        }

        var time = _settings.ScheduledTime;
        int h24 = time.Hours;
        int m = time.Minutes;
        bool isPm = h24 >= 12;
        int h12 = h24 % 12;
        if (h12 == 0) h12 = 12;

        SelectComboBoxItem(CmbSchedHour, h12.ToString("D2"));
        SelectComboBoxItem(CmbSchedMinute, m.ToString("D2"));
        if (CmbSchedAmPm != null) CmbSchedAmPm.SelectedIndex = isPm ? 1 : 0;
        UpdateSchedPreview();

        ChkGlobalWakeToRun.IsChecked = _settings.WakeToRun;
        ChkGlobalCatchupIfMissed.IsChecked = _settings.CatchupIfMissed;
        ChkGlobalSleepAfterComplete.IsChecked = _settings.SleepAfterComplete;

        var rs = _settings.RemoteServer;
        ChkRemoteWakeEnabled.IsChecked = rs.WakeOnLanEnabled;
        EdtServerHost.Text = string.IsNullOrWhiteSpace(rs.ServerHostName) ? "FMC-SERVER" : rs.ServerHostName;
        EdtServerMac.Text = rs.ServerMacAddress;
        EdtServerWakeWait.Text = rs.WakeWaitSeconds.ToString();
        EdtServerUser.Text = rs.ServerUsername;
        EdtServerPass.Password = rs.ServerPassword;
        ChkRemoteSleepEnabled.IsChecked = rs.SleepServerAfterComplete;
        EdtRemoteSleepCmd.Text = rs.RemoteSleepCommand;
        if (!string.IsNullOrWhiteSpace(rs.ServerUsername))
        {
            TxtPairStatus.Text = "Credentials configured";
            TxtPairStatus.Foreground = (Brush)FindResource("TextSecondaryBrush");
        }

        var n = _settings.Notifications;
        ChkNtfyEnabled.IsChecked = n.NtfyEnabled;
        EdtNtfyTopic.Text = n.NtfyTopic;

        ChkDiscordEnabled.IsChecked = n.DiscordEnabled;
        EdtDiscordWebhook.Text = n.DiscordWebhookUrl;

        ChkSmtpEnabled.IsChecked = n.SmtpEnabled;
        EdtSmtpHost.Text = n.SmtpHost;
        EdtSmtpPort.Text = n.SmtpPort.ToString();
        ChkSmtpUseSsl.IsChecked = n.SmtpUseSsl;
        EdtSmtpUser.Text = n.SmtpUsername;
        EdtSmtpPass.Password = n.SmtpPassword;
        EdtRecipientEmail.Text = n.RecipientEmail;

        ChkMinimizeOnClose.IsChecked = _settings.MinimizeOnClose;
        ChkStartMinimized.IsChecked = _settings.StartMinimizedToTray;
        EdtDefaultRetention.Text = Math.Max(0, _settings.DefaultArchiveRetentionCount).ToString();

        // Populate Theme preference
        if (string.Equals(_settings.ThemeMode, "Light", StringComparison.OrdinalIgnoreCase))
            ThemeLightRadio.IsChecked = true;
        else if (string.Equals(_settings.ThemeMode, "System", StringComparison.OrdinalIgnoreCase))
            ThemeSystemRadio.IsChecked = true;
        else
            ThemeDarkRadio.IsChecked = true;
    }

    private void OnThemeOptionChanged(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        string mode = "Dark";
        if (ThemeLightRadio?.IsChecked == true) mode = "Light";
        else if (ThemeSystemRadio?.IsChecked == true) mode = "System";

        _settings.ThemeMode = mode;
        ThemeManager.ApplyTheme(mode);
    }

    private void OnTimeZoneSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSchedPreview();
    }

    private void OnSchedTimeChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSchedPreview();
    }

    private void UpdateSchedPreview()
    {
        if (TxtSchedPreview == null || CmbSchedHour == null || CmbSchedMinute == null || CmbSchedAmPm == null) return;
        var hStr = (CmbSchedHour.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "02";
        var mStr = (CmbSchedMinute.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "00";
        var amPm = CmbSchedAmPm.SelectedIndex == 1 ? "PM" : "AM";

        int.TryParse(hStr, out int h12);
        int.TryParse(mStr, out int minute);
        bool isPm = CmbSchedAmPm.SelectedIndex == 1;
        int h24 = h12 % 12;
        if (isPm) h24 += 12;
        var schedTime = new TimeSpan(h24, minute, 0);

        string selectedTzId = (CmbTimeZone?.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings.TimeZoneId ?? "Local";
        var tzAbbrev = TimeZoneHelper.GetShortAbbreviation(selectedTzId);

        if (string.IsNullOrWhiteSpace(selectedTzId) || selectedTzId.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            TxtSchedPreview.Text = $"(Runs daily at {hStr}:{mStr} {amPm} Local)";
        }
        else
        {
            var localTime = TimeZoneHelper.GetLocalScheduledTime(selectedTzId, schedTime);
            int localH24 = localTime.Hours;
            int localM = localTime.Minutes;
            string localAmPm = localH24 >= 12 ? "PM" : "AM";
            int localH12 = localH24 % 12;
            if (localH12 == 0) localH12 = 12;

            TxtSchedPreview.Text = $"(Runs daily at {hStr}:{mStr} {amPm} {tzAbbrev} — {localH12:D2}:{localM:D2} {localAmPm} Local)";
        }
    }

    private static void SelectComboBoxItem(ComboBox? combo, string text)
    {
        if (combo == null) return;
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem ci && ci.Content.ToString() == text)
            {
                combo.SelectedItem = ci;
                return;
            }
        }
        if (combo.Items.Count > 0 && combo.SelectedIndex == -1) combo.SelectedIndex = 0;
    }

    private void OnSaveSettingsClick(object sender, RoutedEventArgs e)
    {
        _settings.ScheduleEnabled = ChkGlobalScheduleEnabled.IsChecked == true;

        if (CmbTimeZone?.SelectedItem is ComboBoxItem selTz && selTz.Tag is string tzId)
        {
            _settings.TimeZoneId = tzId;
            TimeZoneHelper.CurrentTimeZoneId = tzId;
        }

        var hStr = (CmbSchedHour.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "02";
        var mStr = (CmbSchedMinute.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "00";
        int.TryParse(hStr, out int h12);
        int.TryParse(mStr, out int minute);
        bool isPm = CmbSchedAmPm.SelectedIndex == 1;
        int h24 = h12 % 12;
        if (isPm) h24 += 12;
        _settings.ScheduledTime = new TimeSpan(h24, minute, 0);
        _settings.WakeToRun = ChkGlobalWakeToRun.IsChecked == true;
        _settings.CatchupIfMissed = ChkGlobalCatchupIfMissed.IsChecked == true;
        _settings.SleepAfterComplete = ChkGlobalSleepAfterComplete.IsChecked == true;

        var rs = _settings.RemoteServer;
        rs.WakeOnLanEnabled = ChkRemoteWakeEnabled.IsChecked == true;
        rs.ServerHostName = EdtServerHost.Text.Trim();
        rs.ServerMacAddress = EdtServerMac.Text.Trim();
        if (int.TryParse(EdtServerWakeWait.Text, out var waitSec)) rs.WakeWaitSeconds = Math.Max(5, waitSec);
        rs.ServerUsername = EdtServerUser.Text.Trim();
        rs.ServerPassword = EdtServerPass.Password;
        rs.SleepServerAfterComplete = ChkRemoteSleepEnabled.IsChecked == true;
        rs.RemoteSleepCommand = EdtRemoteSleepCmd.Text.Trim();

        var n = _settings.Notifications;
        n.NtfyEnabled = ChkNtfyEnabled.IsChecked == true;
        n.NtfyTopic = EdtNtfyTopic.Text.Trim();

        n.DiscordEnabled = ChkDiscordEnabled.IsChecked == true;
        n.DiscordWebhookUrl = EdtDiscordWebhook.Text.Trim();

        n.SmtpEnabled = ChkSmtpEnabled.IsChecked == true;
        n.SmtpHost = EdtSmtpHost.Text.Trim();
        if (int.TryParse(EdtSmtpPort.Text, out var port)) n.SmtpPort = port;
        n.SmtpUseSsl = ChkSmtpUseSsl.IsChecked == true;
        n.SmtpUsername = EdtSmtpUser.Text.Trim();
        n.SmtpPassword = EdtSmtpPass.Password;
        n.RecipientEmail = EdtRecipientEmail.Text.Trim();

        _settings.MinimizeOnClose = ChkMinimizeOnClose.IsChecked == true;
        _settings.StartMinimizedToTray = ChkStartMinimized.IsChecked == true;

        if (int.TryParse(EdtDefaultRetention.Text.Trim(), out var defRetention))
            _settings.DefaultArchiveRetentionCount = Math.Clamp(defRetention, 0, 999);

        string themeMode = "Dark";
        if (ThemeLightRadio?.IsChecked == true) themeMode = "Light";
        else if (ThemeSystemRadio?.IsChecked == true) themeMode = "System";
        _settings.ThemeMode = themeMode;

        _configService.SaveSettings(_settings);

        // Sync Windows Task Scheduler master task
        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
        if (!string.IsNullOrEmpty(exePath))
        {
            var success = _schedulerService.SyncMasterSchedule(_settings, exePath);
            if (!success && _settings.ScheduleEnabled)
            {
                RefreshDashboard();
                MessageBox.Show(
                    $"Settings saved, but the scheduled task could NOT be registered in Windows Task Scheduler.\n\n" +
                    $"Error: {_schedulerService.LastError}\n\n" +
                    "Try running FopherSync as Administrator once to create the task.",
                    "Task Scheduler Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        RefreshDashboard();
        _ = LoadHistoryRunsAsync();
        MessageBox.Show("Settings saved and master schedule synced with Windows Task Scheduler!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnNtfyHelpClick(object sender, RoutedEventArgs e)
    {
        var helpGuide = new NtfyHelpGuideWindow();
        helpGuide.Owner = this;
        helpGuide.ShowDialog();
    }

    private void OnEmailHelpClick(object sender, RoutedEventArgs e)
    {
        var helpGuide = new EmailHelpGuideWindow();
        helpGuide.Owner = this;
        helpGuide.ShowDialog();
    }

    private async void OnTestNtfyClick(object sender, RoutedEventArgs e)
    {
        var topic = EdtNtfyTopic.Text.Trim();
        if (string.IsNullOrWhiteSpace(topic))
        {
            var result = MessageBox.Show(
                "Please enter an ntfy Topic name before sending a test notification.\n\nWould you like to open the setup guide to see how to pick a topic and connect your phone?",
                "ntfy Topic Required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                OnNtfyHelpClick(sender, e);
            }
            return;
        }

        var ntfy = new NtfyNotifier(new NotificationSettings
        {
            NtfyEnabled = true,
            NtfyTopic = topic
        });

        var (success, msg) = await ntfy.SendTestNotificationAsync();
        if (success)
        {
            MessageBox.Show(msg, "Push Test Successful", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            var result = MessageBox.Show(
                $"{msg}\n\nWould you like to open the ntfy setup guide for troubleshooting?",
                "Push Test Failed",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);

            if (result == MessageBoxResult.Yes)
            {
                OnNtfyHelpClick(sender, e);
            }
        }
    }

    private async void OnTestDiscordClick(object sender, RoutedEventArgs e)
    {
        var discord = new DiscordNotifier(new NotificationSettings
        {
            DiscordEnabled = true,
            DiscordWebhookUrl = EdtDiscordWebhook.Text.Trim()
        });

        var (success, msg) = await discord.SendTestNotificationAsync();
        MessageBox.Show(msg, success ? "Discord Test Successful" : "Discord Test Failed", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Error);
    }

    private async void OnTestEmailClick(object sender, RoutedEventArgs e)
    {
        int.TryParse(EdtSmtpPort.Text, out var port);
        var smtp = new SmtpNotifier(new NotificationSettings
        {
            SmtpEnabled = true,
            SmtpHost = EdtSmtpHost.Text.Trim(),
            SmtpPort = port > 0 ? port : 587,
            SmtpUseSsl = ChkSmtpUseSsl.IsChecked == true,
            SmtpUsername = EdtSmtpUser.Text.Trim(),
            SmtpPassword = EdtSmtpPass.Password,
            RecipientEmail = EdtRecipientEmail.Text.Trim()
        });

        var (success, msg) = await smtp.SendTestNotificationAsync();
        if (success)
        {
            MessageBox.Show(msg, "Email Test Successful", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            bool isAuthIssue = msg.Contains("5.7.8", StringComparison.OrdinalIgnoreCase) ||
                               msg.Contains("BadCredentials", StringComparison.OrdinalIgnoreCase) ||
                               msg.Contains("Username and Password not accepted", StringComparison.OrdinalIgnoreCase) ||
                               msg.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase);

            var dlg = new EmailErrorDialog(msg, isAuthError: isAuthIssue)
            {
                Owner = this
            };
            dlg.ShowDialog();
        }
    }

    private async void OnAutoDetectMacClick(object sender, RoutedEventArgs e)
    {
        var host = EdtServerHost.Text.Trim();
        if (string.IsNullOrWhiteSpace(host)) host = "FMC-SERVER";

        BtnAutoDetectMac.IsEnabled = false;
        BtnAutoDetectMac.Content = "Detecting...";
        try
        {
            var mac = await WakeOnLanService.AutoDetectMacAddressAsync(host);
            if (!string.IsNullOrWhiteSpace(mac))
            {
                EdtServerMac.Text = mac;
                MessageBox.Show($"Successfully detected MAC Address for '{host}':\n\n{mac}", "MAC Detected", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Could not automatically detect MAC for '{host}'.\n\nPlease ensure the server is turned on and connected to the network, or find its MAC address via 'getmac' on the server.", "Auto-Detect", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            BtnAutoDetectMac.IsEnabled = true;
            BtnAutoDetectMac.Content = "🔍 Auto-Detect";
        }
    }

    private async void OnTestWakeClick(object sender, RoutedEventArgs e)
    {
        var mac = EdtServerMac.Text.Trim();
        var host = EdtServerHost.Text.Trim();

        if (string.IsNullOrWhiteSpace(mac))
        {
            MessageBox.Show("Please enter a valid MAC address (e.g. 00:11:22:33:44:55) or click '🔍 Auto-Detect'.", "Missing MAC", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnTestWake.IsEnabled = false;
        BtnTestWake.Content = "Broadcasting...";

        try
        {
            var sent = WakeOnLanService.SendWakeOnLan(mac);
            if (!sent)
            {
                MessageBox.Show("Invalid MAC address format. Please enter 12 hexadecimal characters (e.g. 00:11:22:33:44:55).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var isOnline = await WakeOnLanService.PingHostAsync(host, 1500);
            var status = isOnline ? $"Server '{host}' is ONLINE and responding to network pings!" : $"Wake-on-LAN Magic Packet broadcasted to {mac}.\n\nIf the server was asleep, it should power on shortly.";
            MessageBox.Show(status, "Wake-on-LAN Test", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            BtnTestWake.IsEnabled = true;
            BtnTestWake.Content = "⚡ Test Wake";
        }
    }

    private async void OnPairServerClick(object sender, RoutedEventArgs e)
    {
        var host = EdtServerHost.Text.Trim();
        var user = EdtServerUser.Text.Trim();
        var pass = EdtServerPass.Password;

        if (string.IsNullOrWhiteSpace(host)) host = "FMC-SERVER";

        BtnPairServer.IsEnabled = false;
        BtnPairServer.Content = "Pairing...";
        TxtPairStatus.Text = "⏳ Testing connection...";
        TxtPairStatus.Foreground = (Brush)FindResource("AccentBlueBrush");

        try
        {
            var (success, msg) = await WakeOnLanService.PairAndTestServerAsync(host, user, pass);
            if (success)
            {
                TxtPairStatus.Text = "✅ Paired & Verified";
                TxtPairStatus.Foreground = (Brush)FindResource("SuccessGreenBrush");

                // Auto-save the credentials in settings
                _settings.RemoteServer.ServerHostName = host;
                _settings.RemoteServer.ServerUsername = user;
                _settings.RemoteServer.ServerPassword = pass;
                _configService.SaveSettings(_settings);

                MessageBox.Show(
                    $"Successfully paired with '{host}'!\n\n{msg}\n\nRemote sleep commands are now authorized.",
                    "Server Pairing Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                TxtPairStatus.Text = "⚠️ Pairing Incomplete";
                TxtPairStatus.Foreground = (Brush)FindResource("WarningAmberBrush");
                MessageBox.Show(
                    $"Could not complete pairing with '{host}':\n\n{msg}",
                    "Server Pairing Notice",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            BtnPairServer.IsEnabled = true;
            BtnPairServer.Content = "🛠️ Pair Windows Server";
        }
    }

    private async void OnTestRemoteSleepClick(object sender, RoutedEventArgs e)
    {
        var host = EdtServerHost.Text.Trim();
        var cmd = EdtRemoteSleepCmd.Text.Trim();
        var user = EdtServerUser.Text.Trim();
        var pass = EdtServerPass.Password;

        if (string.IsNullOrWhiteSpace(host)) host = "FMC-SERVER";

        var confirm = MessageBox.Show(
            $"Are you sure you want to test putting '{host}' to sleep right now?",
            "Confirm Remote Sleep Test",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        BtnTestRemoteSleep.IsEnabled = false;
        BtnTestRemoteSleep.Content = "Sending...";

        try
        {
            var (success, msg) = await WakeOnLanService.ExecuteRemoteSleepAsync(host, cmd, user, pass);
            MessageBox.Show(
                success ? $"Remote sleep signal sent to '{host}':\n\n{msg}" : $"Remote sleep command returned:\n\n{msg}",
                success ? "Success" : "Notice",
                MessageBoxButton.OK,
                success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        finally
        {
            BtnTestRemoteSleep.IsEnabled = true;
            BtnTestRemoteSleep.Content = "⚡ Test Sleep Signal";
        }
    }

    #endregion

    #region System Tray & Window Lifecycle

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!_isExplicitExit && _settings.MinimizeOnClose)
        {
            e.Cancel = true;
            Hide();
            try { MyTaskbarIcon.ShowNotification("FopherSync", "FopherSync is running in the background. Double-click to open."); } catch { }
        }
    }

    private void OnTrayDoubleClicked(object sender, RoutedEventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnTrayOpenClick(object sender, RoutedEventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private async void OnTrayRunAllClick(object sender, RoutedEventArgs e)
    {
        var scheduledJobs = _jobs.Where(j => j.IsScheduled).ToList();
        if (scheduledJobs.Count == 0) scheduledJobs = _jobs;

        if (_settings.RemoteServer.WakeOnLanEnabled && !string.IsNullOrWhiteSpace(_settings.RemoteServer.ServerMacAddress))
        {
            await WakeOnLanService.WakeAndAwaitAsync(
                _settings.RemoteServer.ServerMacAddress,
                _settings.RemoteServer.ServerHostName,
                _settings.RemoteServer.WakeWaitSeconds);
        }

        foreach (var job in scheduledJobs)
        {
            await ExecuteJobAsync(job, isDryRun: false);
        }

        if (_settings.RemoteServer.SleepServerAfterComplete)
        {
            await WakeOnLanService.ExecuteRemoteSleepAsync(
                _settings.RemoteServer.ServerHostName,
                _settings.RemoteServer.RemoteSleepCommand);
        }

        if (_settings.SleepAfterComplete)
        {
            var sleepDialog = new SleepCountdownDialog("All Scheduled Backups");
            sleepDialog.Show();
        }
    }

    private void OnTrayExitClick(object sender, RoutedEventArgs e)
    {
        _isExplicitExit = true;
        MyTaskbarIcon.Dispose();
        Close();
        Application.Current.Shutdown();
    }

    private void OnAppQuitClick(object sender, RoutedEventArgs e)
    {
        OnTrayExitClick(sender, e);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private void InitializeAppIcon()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var assetsDir = Path.Combine(baseDir, "Assets");
            var localPng = Path.Combine(assetsDir, "app_icon.png");

            var candidates = new[]
            {
                Path.Combine(assetsDir, "app_icon.jpg"),
                Path.Combine(baseDir, "FopherSync Icon.jpg"),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\FopherSync Icon.jpg")),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\FopherSync Icon.jpg")),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\FopherSync Icon.jpg")),
                @"c:\Users\cmcha\Desktop\Fopher\Code\FopherSync\FopherSync Icon.jpg"
            };

            string? srcJpg = candidates.FirstOrDefault(File.Exists);

            // Generate studio-quality transparent PNG and multi-resolution ICO from source JPG
            if (srcJpg != null)
            {
                try
                {
                    Directory.CreateDirectory(assetsDir);
                    using var srcBmp = new System.Drawing.Bitmap(srcJpg);

                    // 1. Process at full native resolution: unmix white background and recover true edge colors
                    using var fullResTransparent = RemoveWhiteBackgroundAndUnmix(srcBmp);

                    // 2. Generate multi-resolution icons (256, 128, 64, 48, 32, 16) with HighQualityBicubic sampling
                    var targetSizes = new[] { 256, 128, 64, 48, 32, 16 };
                    var frames = new List<(int size, byte[] pngBytes)>();

                    foreach (var size in targetSizes)
                    {
                        using var square = CreateSquareIcon(fullResTransparent, size);
                        using var ms = new MemoryStream();
                        square.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        var bytes = ms.ToArray();
                        frames.Add((size, bytes));

                        // Save 256x256 as the primary high-res PNG for WPF
                        if (size == 256)
                        {
                            square.Save(localPng, System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }

                    // 3. Save standard multi-resolution ICO file
                    var icoPath = Path.Combine(assetsDir, "app.ico");
                    SaveMultiResolutionIco(frames, icoPath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Could not process high-res transparent icon: {ex.Message}");
                }
            }

            if (File.Exists(localPng))
            {
                // 1. Set WPF Window Icon
                var bitmapFrame = BitmapFrame.Create(new Uri(localPng, UriKind.Absolute));
                this.Icon = bitmapFrame;

                if (ImgBrandLogo != null)
                    ImgBrandLogo.Source = bitmapFrame;
                if (ImgAboutLogo != null)
                    ImgAboutLogo.Source = bitmapFrame;

                // 2. Set Tray Icon directly using native Win32 HICON to avoid H.NotifyIcon stream bug
                using var gdiBitmap = new System.Drawing.Bitmap(localPng);
                var hIcon = gdiBitmap.GetHicon();
                try
                {
                    using var icon = System.Drawing.Icon.FromHandle(hIcon);
                    MyTaskbarIcon.Icon = (System.Drawing.Icon)icon.Clone();
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }

            // Remove any legacy "Automatic Backup Sentry" description and lock icon location
            var resolvedIco = Path.Combine(assetsDir, "app.ico");
            FixDesktopShortcuts(resolvedIco);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not load application icon: {ex.Message}");
        }
    }

    private static System.Drawing.Bitmap RemoveWhiteBackgroundAndUnmix(System.Drawing.Bitmap src)
    {
        int w = src.Width;
        int h = src.Height;
        var transparent = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        var srcData = src.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var dstData = transparent.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        int bytes = Math.Abs(srcData.Stride) * h;
        byte[] srcBytes = new byte[bytes];
        byte[] dstBytes = new byte[bytes];

        System.Runtime.InteropServices.Marshal.Copy(srcData.Scan0, srcBytes, 0, bytes);

        for (int y = 0; y < h; y++)
        {
            int rowOffset = y * srcData.Stride;
            for (int x = 0; x < w; x++)
            {
                int idx = rowOffset + (x * 4);
                byte b = srcBytes[idx + 0];
                byte g = srcBytes[idx + 1];
                byte r = srcBytes[idx + 2];

                int minC = Math.Min(r, Math.Min(g, b));
                int dist = 255 - minC;

                if (dist <= 12)
                {
                    // Pure / near-white background (accounting for JPEG compression)
                    dstBytes[idx + 0] = 0;
                    dstBytes[idx + 1] = 0;
                    dstBytes[idx + 2] = 0;
                    dstBytes[idx + 3] = 0;
                }
                else
                {
                    // Compute accurate alpha matting curve
                    float alphaF = Math.Clamp((dist - 10f) / (205f - 10f), 0f, 1f);
                    int alpha = (int)(alphaF * 255f);

                    // De-multiply white background: C_true = (C_obs - 255*(1-alphaF)) / alphaF
                    int unmixedR = Math.Clamp((int)((r - 255f * (1f - alphaF)) / alphaF), 0, 255);
                    int unmixedG = Math.Clamp((int)((g - 255f * (1f - alphaF)) / alphaF), 0, 255);
                    int unmixedB = Math.Clamp((int)((b - 255f * (1f - alphaF)) / alphaF), 0, 255);

                    dstBytes[idx + 0] = (byte)unmixedB;
                    dstBytes[idx + 1] = (byte)unmixedG;
                    dstBytes[idx + 2] = (byte)unmixedR;
                    dstBytes[idx + 3] = (byte)alpha;
                }
            }
        }

        System.Runtime.InteropServices.Marshal.Copy(dstBytes, 0, dstData.Scan0, bytes);
        src.UnlockBits(srcData);
        transparent.UnlockBits(dstData);
        return transparent;
    }

    private static System.Drawing.Bitmap CreateSquareIcon(System.Drawing.Bitmap fullResTransparent, int targetSize)
    {
        var square = new System.Drawing.Bitmap(targetSize, targetSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(square);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;

        int maxDim = Math.Max(fullResTransparent.Width, fullResTransparent.Height);
        float scale = (float)targetSize / maxDim;
        int drawW = (int)Math.Round(fullResTransparent.Width * scale);
        int drawH = (int)Math.Round(fullResTransparent.Height * scale);
        int drawX = (targetSize - drawW) / 2;
        int drawY = (targetSize - drawH) / 2;

        g.DrawImage(fullResTransparent, new System.Drawing.Rectangle(drawX, drawY, drawW, drawH));
        return square;
    }

    private static void SaveMultiResolutionIco(List<(int size, byte[] pngBytes)> frames, string icoPath)
    {
        using var fs = File.Create(icoPath);
        using var bw = new BinaryWriter(fs);

        bw.Write((ushort)0);
        bw.Write((ushort)1);
        bw.Write((ushort)frames.Count);

        int currentOffset = 6 + (16 * frames.Count);

        foreach (var frame in frames)
        {
            byte dim = (byte)(frame.size == 256 ? 0 : frame.size);
            bw.Write(dim);
            bw.Write(dim);
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)1);
            bw.Write((ushort)32);
            bw.Write((uint)frame.pngBytes.Length);
            bw.Write((uint)currentOffset);

            currentOffset += frame.pngBytes.Length;
        }

        foreach (var frame in frames)
        {
            bw.Write(frame.pngBytes);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    private static void FixDesktopShortcuts(string icoPath)
    {
        try
        {
            var searchDirs = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "FopherSync"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "FopherSync")
            };

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    foreach (var dir in searchDirs)
                    {
                        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

                        var oldLnk = Path.Combine(dir, "Automatic Backup Sentry.lnk");
                        var newLnk = Path.Combine(dir, "FopherSync.lnk");
                        if (File.Exists(oldLnk))
                        {
                            try
                            {
                                if (File.Exists(newLnk)) File.Delete(oldLnk);
                                else File.Move(oldLnk, newLnk);
                            }
                            catch { }
                        }

                        foreach (var lnkPath in Directory.GetFiles(dir, "*.lnk"))
                        {
                            try
                            {
                                dynamic shortcut = shell.CreateShortcut(lnkPath);
                                string targetPath = (string)(shortcut.TargetPath ?? "");
                                string desc = (string)(shortcut.Description ?? "");

                                bool isFopherSync = targetPath.EndsWith("FopherSync.exe", StringComparison.OrdinalIgnoreCase)
                                                   || lnkPath.EndsWith("FopherSync.lnk", StringComparison.OrdinalIgnoreCase)
                                                   || lnkPath.Contains("FopherSync", StringComparison.OrdinalIgnoreCase);

                                if (isFopherSync || desc.Contains("Automatic Backup Sentry", StringComparison.OrdinalIgnoreCase) || desc.Contains("Sentry", StringComparison.OrdinalIgnoreCase))
                                {
                                    shortcut.Description = "FopherSync";
                                    if (File.Exists(icoPath))
                                    {
                                        shortcut.IconLocation = $"{icoPath},0";
                                    }
                                    shortcut.Save();
                                }
                            }
                            catch { }
                        }
                    }
                }
            }

            // Update folder desktop.ini icons for project and parent folders
            var candidateFolders = new[]
            {
                @"c:\Users\cmcha\Desktop\Fopher\Code\FopherSync",
                @"c:\Users\cmcha\Desktop\Fopher",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Fopher"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "FopherSync"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "FopherSync"),
                AppDomain.CurrentDomain.BaseDirectory
            };

            foreach (var folder in candidateFolders)
            {
                UpdateFolderIcon(folder, icoPath);
            }

            // Flush Windows Explorer icon cache
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not update shortcuts / folder icons: {ex.Message}");
        }
    }

    private static void UpdateFolderIcon(string folderPath, string icoPath)
    {
        try
        {
            if (!Directory.Exists(folderPath) || !File.Exists(icoPath)) return;
            var iniPath = Path.Combine(folderPath, "desktop.ini");

            if (File.Exists(iniPath) || folderPath.EndsWith("FopherSync", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(iniPath))
                {
                    File.SetAttributes(iniPath, FileAttributes.Normal);
                }
                var content = $"[.ShellClassInfo]\r\nIconResource={icoPath},0\r\n[ViewState]\r\nMode=\r\nVid=\r\nFolderType=Generic\r\n";
                File.WriteAllText(iniPath, content, System.Text.Encoding.Unicode);
                File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System);

                var folderInfo = new DirectoryInfo(folderPath);
                folderInfo.Attributes |= FileAttributes.ReadOnly;
            }
        }
        catch { }
    }

    #endregion
}
