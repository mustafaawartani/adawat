using System;
using System.Windows;
using System.Windows.Media;
using Adawat.Core;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Adawat.UI;

/// <summary>Light/dark theme and accent color, following Windows unless the user picks one.</summary>
public static class ThemeHelper
{
    private static ThemeMode _mode = ThemeMode.System;

    public static event Action? Changed;

    public static bool IsDark => _mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => SystemUsesDarkTheme(),
    };

    public static Color AccentColor
    {
        get
        {
            try
            {
                if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null) is int abgr)
                {
                    var c = Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
                    return IsDark ? Lighten(c, 0.25) : c;
                }
            }
            catch
            {
            }
            return IsDark ? Color.FromRgb(0x4C, 0xC2, 0xFF) : Color.FromRgb(0x00, 0x67, 0xC0);
        }
    }

    public static void Initialize(ThemeMode mode)
    {
        _mode = mode;
        Apply();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(Apply));
            }
        };
    }

    public static void SetMode(ThemeMode mode)
    {
        _mode = mode;
        Apply();
    }

    private static void Apply()
    {
        try
        {
            ApplicationThemeManager.Apply(IsDark ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.Mica, true);
            ApplicationAccentColorManager.Apply(AccentColor, IsDark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        }
        catch (Exception ex)
        {
            Log.Error("Could not apply the theme", ex);
        }
        Changed?.Invoke();
    }

    private static bool SystemUsesDarkTheme()
    {
        try
        {
            var value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
            return value is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }

    private static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount),
        (byte)(c.G + (255 - c.G) * amount),
        (byte)(c.B + (255 - c.B) * amount));
}
