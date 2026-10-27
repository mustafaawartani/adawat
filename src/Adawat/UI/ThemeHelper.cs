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

    /// <summary>
    /// Windows 11 22H2 and later can put a frosted-glass (acrylic) backdrop behind a window.
    /// Elsewhere the windows use solid colors.
    /// </summary>
    public static bool GlassSupported { get; } = Environment.OSVersion.Version.Build >= 22621;

    private static ResourceDictionary? _appColors;

    private static void Apply()
    {
        try
        {
            var theme = IsDark ? ApplicationTheme.Dark : ApplicationTheme.Light;
            ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, false);
            ApplicationAccentColorManager.Apply(AccentColor, theme);
            ApplyAppColors();
        }
        catch (Exception ex)
        {
            Log.Error("Could not apply the theme", ex);
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// The accent colors the whole app, not only its buttons: window backgrounds, cards, fields and
    /// hover states get a touch of it, see-through over the glass where Windows supports it. These
    /// replace WPF UI's own colors (a later dictionary wins; WPF UI swaps its theme in place).
    /// </summary>
    private static void ApplyAppColors()
    {
        var app = Application.Current;
        if (app == null) return;
        if (_appColors == null)
        {
            _appColors = new ResourceDictionary();
            app.Resources.MergedDictionaries.Add(_appColors);
        }
        bool dark = IsDark;
        bool glass = GlassSupported;
        var accent = AccentColor;
        void Set(string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            _appColors[key] = brush;
        }
        Color Alpha(Color c, double opacity) => Color.FromArgb((byte)Math.Round(255 * opacity), c.R, c.G, c.B);

        var card = dark ? Blend(Color.FromRgb(0x2E, 0x2F, 0x34), accent, 0.07) : Blend(Colors.White, accent, 0.035);
        var control = dark ? Blend(Color.FromRgb(0x36, 0x37, 0x3C), accent, 0.08) : Blend(Colors.White, accent, 0.03);
        var background = dark ? Blend(Color.FromRgb(0x20, 0x20, 0x24), accent, 0.10) : Blend(Color.FromRgb(0xF3, 0xF4, 0xF7), accent, 0.08);
        Set("ApplicationBackgroundBrush", background);
        Set("CardBackgroundFillColorDefaultBrush", glass ? Alpha(card, dark ? 0.62 : 0.72) : card);
        Set("CardBackgroundFillColorSecondaryBrush", glass ? Alpha(card, dark ? 0.45 : 0.55) : Blend(card, accent, 0.04));
        Set("CardStrokeColorDefaultBrush", Alpha(dark ? Lighten(accent, 0.2) : accent, dark ? 0.16 : 0.14));
        Set("ControlFillColorDefaultBrush", glass ? Alpha(control, dark ? 0.7 : 0.8) : control);
        Set("ControlFillColorSecondaryBrush", Alpha(Blend(control, accent, 0.06), glass ? 0.65 : 1));
        Set("ControlFillColorTertiaryBrush", Alpha(Blend(control, accent, 0.03), glass ? 0.5 : 1));
        Set("ControlFillColorInputActiveBrush", dark ? Color.FromRgb(0x1E, 0x1F, 0x23) : Colors.White);
        Set("SubtleFillColorSecondaryBrush", Alpha(accent, dark ? 0.14 : 0.09));
        Set("SubtleFillColorTertiaryBrush", Alpha(accent, dark ? 0.10 : 0.06));
        // Laid over the whole settings window: the color shows through the glass.
        Set("Adawat.WindowTint", glass ? Alpha(accent, dark ? 0.12 : 0.09) : background);
        ApplyPalette(_appColors);
    }

    /// <summary>
    /// Puts the palette into <paramref name="resources"/> as brushes: Adawat.Surface (solid window
    /// color), Adawat.Glass (the window color over the glass backdrop), Adawat.Subtle (cards),
    /// Adawat.Hover, Adawat.Border, Adawat.Text, Adawat.Muted, Adawat.Faint, Adawat.Accent,
    /// Adawat.AccentSoft (tinted backgrounds), Adawat.OnAccent (text on the accent), Adawat.Tint
    /// and Adawat.TintBorder (the translation area). The neutrals carry a little of the accent.
    /// </summary>
    public static void ApplyPalette(ResourceDictionary resources)
    {
        bool dark = IsDark;
        bool glass = GlassSupported;
        var accent = AccentColor;
        void Set(string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources["Adawat." + key] = brush;
        }
        Color Alpha(Color c, double opacity) => Color.FromArgb((byte)Math.Round(255 * opacity), c.R, c.G, c.B);

        var surface = dark ? Blend(Color.FromRgb(0x1F, 0x20, 0x24), accent, 0.06) : Blend(Colors.White, accent, 0.03);
        Set("Surface", surface);
        Set("Glass", glass ? Alpha(surface, dark ? 0.62 : 0.68) : surface);
        Set("Subtle", dark ? Blend(Color.FromRgb(0x2A, 0x2B, 0x30), accent, 0.12) : Blend(Color.FromRgb(0xF3, 0xF5, 0xF8), accent, 0.08));
        Set("Hover", dark ? Blend(Color.FromRgb(0x33, 0x35, 0x3B), accent, 0.16) : Blend(Color.FromRgb(0xEC, 0xEF, 0xF4), accent, 0.12));
        Set("Border", dark ? Blend(Color.FromRgb(0x3A, 0x3C, 0x42), accent, 0.15) : Blend(Color.FromRgb(0xE3, 0xE7, 0xED), accent, 0.14));
        Set("Text", dark ? Color.FromRgb(0xF1, 0xF3, 0xF6) : Blend(Color.FromRgb(0x14, 0x1A, 0x24), accent, 0.08));
        Set("Muted", dark ? Blend(Color.FromRgb(0xA4, 0xAB, 0xB8), accent, 0.12) : Blend(Color.FromRgb(0x5E, 0x6A, 0x7D), accent, 0.14));
        Set("Faint", dark ? Color.FromRgb(0x70, 0x77, 0x84) : Color.FromRgb(0x9A, 0xA4, 0xB2));
        Set("Accent", accent);
        Set("AccentSoft", Color.FromArgb(dark ? (byte)0x33 : (byte)0x1C, accent.R, accent.G, accent.B));
        Set("OnAccent", dark ? Color.FromRgb(0x0B, 0x10, 0x18) : Colors.White);
        // The translation area: clearly in the accent color, still light enough to read.
        var tint = Blend(surface, accent, dark ? 0.16 : 0.11);
        Set("Tint", glass ? Alpha(tint, dark ? 0.72 : 0.78) : tint);
        Set("TintBorder", Blend(surface, accent, dark ? 0.26 : 0.2));
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
