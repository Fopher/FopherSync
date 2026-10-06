using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FopherSync.Core.Services;

namespace FopherSync.Wpf;

public partial class SleepCountdownDialog : Window
{
    private readonly DispatcherTimer _timer = new();
    private int _secondsRemaining = 60;
    private bool _actionTaken = false;

    public SleepCountdownDialog(string jobName = "", string? customMessage = null)
    {
        InitializeComponent();

        if (!string.IsNullOrEmpty(customMessage))
        {
            TxtMessage.Text = $"{customMessage} The computer will enter sleep mode in 60 seconds to conserve energy.";
        }
        else if (!string.IsNullOrEmpty(jobName))
        {
            TxtMessage.Text = $"Backup job '{jobName}' has completed. The computer will enter sleep mode in 60 seconds to conserve energy.";
        }

        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += OnTimerTick;
        _timer.Start();

        Loaded += (s, e) =>
        {
            try { SystemSounds.Asterisk.Play(); } catch { }
            Activate();
        };

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                CancelSleep();
            }
        };
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _secondsRemaining--;

        TxtSecondsRemaining.Text = $"{_secondsRemaining} {(_secondsRemaining == 1 ? "second" : "seconds")}";
        PrgCountdown.Value = _secondsRemaining;

        if (_secondsRemaining <= 0)
        {
            _timer.Stop();
            ExecuteSleep();
        }
    }

    private void OnSleepNowClick(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        ExecuteSleep();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        CancelSleep();
    }

    private void CancelSleep()
    {
        if (_actionTaken) return;
        _actionTaken = true;
        _timer.Stop();
        Close();
    }

    private void ExecuteSleep()
    {
        if (_actionTaken) return;
        _actionTaken = true;
        _timer.Stop();
        Close();
        PowerService.Sleep();
    }
}
