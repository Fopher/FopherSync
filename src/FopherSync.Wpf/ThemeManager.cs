using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace FopherSync.Wpf;

public static class ThemeManager
{
    private static string _currentMode = "Dark";
    private static bool _isHooked = false;

    public static string CurrentMode => _currentMode;

    public static void Initialize(string initialMode)
    {
        _currentMode = string.IsNullOrWhiteSpace(initialMode) ? "Dark" : initialMode;

        if (!_isHooked)
        {
            try
            {
                SystemEvents.UserPreferenceChanged += (s, e) =>
                {
                    if (e.Category == UserPreferenceCategory.General)
                    {
                        if (string.Equals(_currentMode, "System", StringComparison.OrdinalIgnoreCase))
                        {
                            Application.Current?.Dispatcher.Invoke(() => ApplyTheme("System"));
                        }
                    }
                };
                _isHooked = true;
            }
            catch { }
        }

        ApplyTheme(_currentMode);
    }

    public static void ApplyTheme(string themeMode)
    {
        _currentMode = string.IsNullOrWhiteSpace(themeMode) ? "Dark" : themeMode;

        bool isLight = false;

        if (string.Equals(_currentMode, "Light", StringComparison.OrdinalIgnoreCase))
        {
            isLight = true;
        }
        else if (string.Equals(_currentMode, "System", StringComparison.OrdinalIgnoreCase))
        {
            isLight = IsSystemInLightTheme();
        }
        else
        {
            isLight = false; // Default Dark
        }

        ApplyPalette(isLight);
    }

    public static bool IsSystemInLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key != null)
            {
                var val = key.GetValue("AppsUseLightTheme");
                if (val is int intVal)
                {
                    return intVal == 1;
                }
            }
        }
        catch { }
        return false;
    }

    private static void ApplyPalette(bool isLight)
    {
        var app = Application.Current;
        if (app == null) return;

        if (isLight)
        {
            SetColorAndBrush(app, "BgDarkestColor", Color.FromRgb(0xF3, 0xF4, 0xF6)); // #F3F4F6 soft light gray canvas
            SetColorAndBrush(app, "BgDarkColor", Color.FromRgb(0xFF, 0xFF, 0xFF));    // #FFFFFF pure white cards/navbar
            SetColorAndBrush(app, "BgCardColor", Color.FromRgb(0xFF, 0xFF, 0xFF));    // #FFFFFF pure white cards
            SetColorAndBrush(app, "BgCardHoverColor", Color.FromRgb(0xEA, 0xEE, 0xF2)); // #EAEEF2 subtle light hover
            SetColorAndBrush(app, "BorderSubtleColor", Color.FromRgb(0xDE, 0xE2, 0xE6)); // #DEE2E6 clean light border
            SetColorAndBrush(app, "TextPrimaryColor", Color.FromRgb(0x11, 0x18, 0x27));  // #111827 deep slate black
            SetColorAndBrush(app, "TextSecondaryColor", Color.FromRgb(0x6B, 0x72, 0x80));// #6B7280 muted slate gray
            SetColorAndBrush(app, "AccentBlueColor", Color.FromRgb(0x25, 0x63, 0xEB));   // #2563EB royal blue
            SetColorAndBrush(app, "AccentBlueHoverColor", Color.FromRgb(0x1D, 0x4E, 0xD8));
            SetColorAndBrush(app, "SuccessGreenColor", Color.FromRgb(0x05, 0x96, 0x69));
            SetColorAndBrush(app, "WarningAmberColor", Color.FromRgb(0xD9, 0x77, 0x06));
            SetColorAndBrush(app, "DangerRedColor", Color.FromRgb(0xDC, 0x26, 0x26));
        }
        else
        {
            SetColorAndBrush(app, "BgDarkestColor", Color.FromRgb(0x0F, 0x0F, 0x12)); // #0F0F12
            SetColorAndBrush(app, "BgDarkColor", Color.FromRgb(0x18, 0x18, 0x1C));    // #18181C
            SetColorAndBrush(app, "BgCardColor", Color.FromRgb(0x20, 0x20, 0x26));    // #202026
            SetColorAndBrush(app, "BgCardHoverColor", Color.FromRgb(0x28, 0x28, 0x30)); // #282830
            SetColorAndBrush(app, "BorderSubtleColor", Color.FromRgb(0x2E, 0x2E, 0x38)); // #2E2E38
            SetColorAndBrush(app, "TextPrimaryColor", Color.FromRgb(0xF3, 0xF4, 0xF6));  // #F3F4F6
            SetColorAndBrush(app, "TextSecondaryColor", Color.FromRgb(0x9C, 0xA3, 0xAF));// #9CA3AF
            SetColorAndBrush(app, "AccentBlueColor", Color.FromRgb(0x3B, 0x82, 0xF6));   // #3B82F6
            SetColorAndBrush(app, "AccentBlueHoverColor", Color.FromRgb(0x25, 0x63, 0xEB));
            SetColorAndBrush(app, "SuccessGreenColor", Color.FromRgb(0x10, 0xB9, 0x81));
            SetColorAndBrush(app, "WarningAmberColor", Color.FromRgb(0xF5, 0x9E, 0x0B));
            SetColorAndBrush(app, "DangerRedColor", Color.FromRgb(0xEF, 0x44, 0x44));
        }
    }

    private static void SetColorAndBrush(Application app, string colorKey, Color color)
    {
        app.Resources[colorKey] = color;

        var brushKey = colorKey.Replace("Color", "Brush");
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        app.Resources[brushKey] = brush;
    }
}
