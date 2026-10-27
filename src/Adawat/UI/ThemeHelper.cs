using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Adawat.Core;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Adawat.UI;

/// <summary>
/// Light/dark theme and accent color for the whole app. Also provides the small palette the
/// translator popup and the image preview are drawn with, as brushes under "Adawat.*" keys.
/// </summary>
public static class ThemeHelper
{
    private static ThemeMode _mode = ThemeMode.Light;
    private static AccentChoice _accent = AccentChoice.Blue;

    /// <summary>The accent colors offered in the settings (as used on a light background).</summary>
    public static readonly IReadOnlyDictionary<AccentChoice, Color> AccentColors = new Dictionary<AccentChoice, Color>
    {
        [AccentChoice.Blue] = Color.FromRgb(0x25, 0x63, 0xEB),
        [AccentChoice.Indigo] = Color.FromRgb(0x4F, 0x46, 0xE5),
        [AccentChoice.Violet] = Color.FromRgb(0x7C, 0x3A, 0xED),
        [AccentChoice.Teal] = Color.FromRgb(0x0D, 0x94, 0x88),
        [AccentChoice.Green] = Color.FromRgb(0x16, 0xA3, 0x4A),
        [AccentChoice.Rose] = Color.FromRgb(0xE1, 0x1D, 0x48),
        [AccentChoice.Orange] = Color.FromRgb(0xEA, 0x58, 0x0C),
        [AccentChoice.Graphite] = Color.FromRgb(0x47, 0x55, 0x69),
    };

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
            var color = AccentColors.TryGetValue(_accent, out var chosen) ? chosen : WindowsAccent();
            return IsDark ? Lighten(color, 0.3) : color;
        }
    }

    public static void Initialize(ThemeMode mode, AccentChoice accent)
    {
        _mode = mode;
        _accent = accent;
        Apply();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(Apply));
            }
        };
    }

    public static void Set(ThemeMode mode, AccentChoice accent)
    {
        if (mode == _mode && accent == _accent) return;
        _mode = mode;
        _accent = accent;
        Apply();
    }

    private static void Apply()
    {
        try
        {
            var theme = IsDark ? ApplicationTheme.Dark : ApplicationTheme.Light;
            ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, false);
            ApplicationAccentColorManager.Apply(AccentColor, theme);
        }
        catch (Exception ex)
        {
            Log.Error("Could not apply the theme", ex);
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Puts the palette into <paramref name="resources"/> as brushes: Adawat.Surface (window),
    /// Adawat.Subtle (cards), Adawat.Hover, Adawat.Border, Adawat.Text, Adawat.Muted, Adawat.Faint,
    /// Adawat.Accent, Adawat.AccentSoft (tinted backgrounds), Adawat.OnAccent (text on the accent),
    /// Adawat.Tint and Adawat.TintBorder (the translation area).
    /// </summary>
    public static void ApplyPalette(ResourceDictionary resources)
    {
        bool dark = IsDark;
        var accent = AccentColor;
        void Set(string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources["Adawat." + key] = brush;
        }
        Set("Surface", dark ? Color.FromRgb(0x1F, 0x20, 0x24) : Color.FromRgb(0xFF, 0xFF, 0xFF));
        Set("Subtle", dark ? Color.FromRgb(0x29, 0x2A, 0x2F) : Color.FromRgb(0xF5, 0xF7, 0xFA));
        Set("Hover", dark ? Color.FromRgb(0x33, 0x35, 0x3B) : Color.FromRgb(0xEC, 0xEF, 0xF4));
        Set("Border", dark ? Color.FromRgb(0x3A, 0x3C, 0x42) : Color.FromRgb(0xE3, 0xE7, 0xED));
        Set("Text", dark ? Color.FromRgb(0xF1, 0xF3, 0xF6) : Color.FromRgb(0x14, 0x1A, 0x24));
        Set("Muted", dark ? Color.FromRgb(0xA4, 0xAB, 0xB8) : Color.FromRgb(0x5E, 0x6A, 0x7D));
        Set("Faint", dark ? Color.FromRgb(0x70, 0x77, 0x84) : Color.FromRgb(0x9A, 0xA4, 0xB2));
        Set("Accent", accent);
        Set("AccentSoft", Color.FromArgb(dark ? (byte)0x33 : (byte)0x1C, accent.R, accent.G, accent.B));
        Set("OnAccent", dark ? Color.FromRgb(0x0B, 0x10, 0x18) : Colors.White);
        // The translation area: the surface with a breath of the accent color.
        var surface = dark ? Color.FromRgb(0x1F, 0x20, 0x24) : Colors.White;
        Set("Tint", Blend(surface, accent, dark ? 0.09 : 0.05));
        Set("TintBorder", Blend(surface, accent, dark ? 0.18 : 0.11));
    }

    /// <summary>The palette color for a key (see <see cref="ApplyPalette"/>), for code that needs a raw color.</summary>
    public static Color PaletteColor(string key)
    {
        var resources = new ResourceDictionary();
        ApplyPalette(resources);
        return resources["Adawat." + key] is SolidColorBrush brush ? brush.Color : Colors.Gray;
    }

    private static Color WindowsAccent()
    {
        try
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null) is int abgr)
                return Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
        }
        catch
        {
        }
        return Color.FromRgb(0x00, 0x67, 0xC0);
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

    private static Color Blend(Color under, Color over, double amount) => Color.FromRgb(
        (byte)(under.R + (over.R - under.R) * amount),
        (byte)(under.G + (over.G - under.G) * amount),
        (byte)(under.B + (over.B - under.B) * amount));

    private static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount),
        (byte)(c.G + (255 - c.G) * amount),
        (byte)(c.B + (255 - c.B) * amount));
}
