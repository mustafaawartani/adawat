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
        var colors = _appColors;
        void Set(string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            colors[key] = brush;
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
    /// Puts the palette into <paramref name="resources"/> as brushes: Adawat.Surface (the solid window
    /// color), Adawat.Subtle (quiet fills), Adawat.Hover, Adawat.Pressed, Adawat.Border, Adawat.Divider,
    /// Adawat.Text, Adawat.Muted, Adawat.Faint, Adawat.Accent, Adawat.AccentSoft (tinted backgrounds),
    /// Adawat.OnAccent (text on the accent) and Adawat.Error.
    /// The neutrals are plain greys: the accent is kept for what can be clicked, so nothing looks muddy.
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

        if (dark)
        {
            Set("Surface", Color.FromRgb(0x1E, 0x1F, 0x22));
            Set("Subtle", Color.FromRgb(0x27, 0x28, 0x2C));
            // See-through, so hover looks right on plain and on tinted backgrounds alike.
            Set("Hover", Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
            Set("Pressed", Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
            Set("Border", Color.FromRgb(0x3A, 0x3C, 0x41));
            Set("Divider", Color.FromRgb(0x2F, 0x31, 0x35));
            Set("Text", Color.FromRgb(0xEC, 0xED, 0xEF));
            Set("Muted", Color.FromRgb(0xA1, 0xA5, 0xAC));
            Set("Faint", Color.FromRgb(0x6F, 0x74, 0x7B));
            Set("OnAccent", Color.FromRgb(0x10, 0x12, 0x16));
            Set("Error", Color.FromRgb(0xF2, 0x8B, 0x82));
        }
        else
        {
            Set("Surface", Colors.White);
            Set("Subtle", Color.FromRgb(0xF5, 0xF6, 0xF8));
            Set("Hover", Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
            Set("Pressed", Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
            Set("Border", Color.FromRgb(0xE3, 0xE5, 0xE9));
            Set("Divider", Color.FromRgb(0xEE, 0xEF, 0xF2));
            Set("Text", Color.FromRgb(0x17, 0x19, 0x1D));
            Set("Muted", Color.FromRgb(0x6A, 0x70, 0x79));
            Set("Faint", Color.FromRgb(0xA2, 0xA7, 0xAF));
            Set("OnAccent", Colors.White);
            Set("Error", Color.FromRgb(0xC5, 0x22, 0x1F));
        }
        Set("Accent", accent);
        Set("AccentSoft", Color.FromArgb(dark ? (byte)0x2E : (byte)0x16, accent.R, accent.G, accent.B));
        // Accent for text and icons: a little deeper on light backgrounds so it reads well.
        Set("AccentText", dark ? Lighten(accent, 0.15) : Blend(accent, Colors.Black, 0.18));
        // The language chips: soft white (light) or a lighter layer (dark) over the colored window.
        Set("AccentChip", dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));
        Set("AccentChipHover", dark ? Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF) : Colors.White);
        Set("AccentLine", dark ? Blend(Color.FromRgb(0x1E, 0x1F, 0x22), accent, 0.45) : Blend(Colors.White, accent, 0.34));

        // The translator's background: the whole window in the accent color, light enough to read
        // on, a touch deeper at the top. One color, not split into two different halves.
        var top = dark ? Blend(Color.FromRgb(0x1E, 0x1F, 0x22), accent, 0.26) : Blend(Colors.White, accent, 0.2);
        var bottom = dark ? Blend(Color.FromRgb(0x1E, 0x1F, 0x22), accent, 0.17) : Blend(Colors.White, accent, 0.13);
        var wash = new LinearGradientBrush(top, bottom, 90);
        wash.Freeze();
        resources["Adawat.Wash"] = wash;

        // The same color, see-through, laid over the frosted-glass backdrop: what is behind the
        // window shows through softly blurred, still clearly in the accent color.
        static Color Alpha(Color c, double opacity) => Color.FromArgb((byte)Math.Round(255 * opacity), c.R, c.G, c.B);
        var glass = new LinearGradientBrush(Alpha(top, dark ? 0.74 : 0.66), Alpha(bottom, dark ? 0.66 : 0.56), 90);
        glass.Freeze();
        resources["Adawat.WashGlass"] = glass;
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
