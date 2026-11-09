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

    /// <summary>
    /// The translator's color choices: one color (both the same) or two colors that flow into each
    /// other from the top-left corner to the bottom-right.
    /// </summary>
    public static readonly IReadOnlyDictionary<TranslatorColors, (Color A, Color B, string Name)> TranslatorPalettes =
        new Dictionary<TranslatorColors, (Color, Color, string)>
        {
            [TranslatorColors.Blue] = (Rgb(0x25, 0x63, 0xEB), Rgb(0x25, 0x63, 0xEB), "Blue"),
            [TranslatorColors.Indigo] = (Rgb(0x4F, 0x46, 0xE5), Rgb(0x4F, 0x46, 0xE5), "Indigo"),
            [TranslatorColors.Violet] = (Rgb(0x7C, 0x3A, 0xED), Rgb(0x7C, 0x3A, 0xED), "Violet"),
            [TranslatorColors.Teal] = (Rgb(0x0D, 0x94, 0x88), Rgb(0x0D, 0x94, 0x88), "Teal"),
            [TranslatorColors.Green] = (Rgb(0x16, 0xA3, 0x4A), Rgb(0x16, 0xA3, 0x4A), "Green"),
            [TranslatorColors.Rose] = (Rgb(0xE1, 0x1D, 0x48), Rgb(0xE1, 0x1D, 0x48), "Rose"),
            [TranslatorColors.Orange] = (Rgb(0xEA, 0x58, 0x0C), Rgb(0xEA, 0x58, 0x0C), "Orange"),
            [TranslatorColors.Graphite] = (Rgb(0x47, 0x55, 0x69), Rgb(0x47, 0x55, 0x69), "Graphite"),
            [TranslatorColors.Ocean] = (Rgb(0x25, 0x63, 0xEB), Rgb(0x14, 0xB8, 0xA6), "Ocean: blue and teal"),
            [TranslatorColors.Sunset] = (Rgb(0xF9, 0x73, 0x16), Rgb(0xE1, 0x1D, 0x48), "Sunset: orange and rose"),
            [TranslatorColors.Aurora] = (Rgb(0x0D, 0x94, 0x88), Rgb(0x7C, 0x3A, 0xED), "Aurora: teal and violet"),
            [TranslatorColors.Berry] = (Rgb(0xDB, 0x27, 0x77), Rgb(0x7C, 0x3A, 0xED), "Berry: pink and violet"),
            [TranslatorColors.Sky] = (Rgb(0x0E, 0xA5, 0xE9), Rgb(0x4F, 0x46, 0xE5), "Sky: light blue and indigo"),
            [TranslatorColors.Peach] = (Rgb(0xF5, 0x9E, 0x0B), Rgb(0xEC, 0x48, 0x99), "Peach: amber and pink"),
            [TranslatorColors.Forest] = (Rgb(0x16, 0xA3, 0x4A), Rgb(0x08, 0x91, 0xB2), "Forest: green and cyan"),
            [TranslatorColors.Dusk] = (Rgb(0x43, 0x38, 0xCA), Rgb(0xDB, 0x27, 0x77), "Dusk: indigo and pink"),
        };

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static TranslatorLook _translatorLook = TranslatorLook.Glass;
    private static TranslatorColors _translatorColors = TranslatorColors.AppAccent;
    private static int _glassClarity = 60;

    /// <summary>The translator's look and colors (see <see cref="ApplyTranslatorPalette"/>).</summary>
    public static void SetTranslatorLook(TranslatorLook look, TranslatorColors colors, int glassClarity)
    {
        glassClarity = Math.Clamp(glassClarity, 1, 100);
        if (look == _translatorLook && colors == _translatorColors && glassClarity == _glassClarity) return;
        _translatorLook = look;
        _translatorColors = colors;
        _glassClarity = glassClarity;
        Changed?.Invoke();
    }

    /// <summary>True when the translator is drawn as frosted glass (chosen, and Windows can do it).</summary>
    public static bool TranslatorGlass => GlassSupported && _translatorLook == TranslatorLook.Glass;

    /// <summary>The translator's two colors as chosen (the same twice for one color), for a light background.</summary>
    public static (Color A, Color B) TranslatorPair(TranslatorColors colors)
    {
        if (TranslatorPalettes.TryGetValue(colors, out var pair)) return (pair.A, pair.B);
        var accent = AccentColors.TryGetValue(_accent, out var chosen) ? chosen : WindowsAccent();
        return (accent, accent);
    }

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
    }

    /// <summary>
    /// The palette of the translator window: <see cref="ApplyPalette"/> in the translator's own colors,
    /// plus Adawat.Background (the whole window: see-through over the glass, or solid color),
    /// Adawat.Sheen (a soft light along the top of the glass) and Adawat.AccentFlow / AccentFlowDown
    /// (both colors, across or downward, for thin lines and bars).
    /// </summary>
    public static void ApplyTranslatorPalette(ResourceDictionary resources) =>
        ApplyColoredPalette(resources, _translatorColors, TranslatorGlass);

    /// <summary>The same palette in solid color (no glass) for another window, such as the quick note box.</summary>
    public static void ApplyNotePalette(ResourceDictionary resources, TranslatorColors colors) =>
        ApplyColoredPalette(resources, colors, glass: false);

    private static void ApplyColoredPalette(ResourceDictionary resources, TranslatorColors colors, bool glass)
    {
        ApplyPalette(resources);
        bool dark = IsDark;
        var (a, b) = TranslatorPair(colors);
        if (dark)
        {
            a = Lighten(a, 0.3);
            b = Lighten(b, 0.3);
        }
        void Set(string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources["Adawat." + key] = brush;
        }
        static Color Alpha(Color c, double opacity) => Color.FromArgb((byte)Math.Round(255 * Math.Clamp(opacity, 0, 1)), c.R, c.G, c.B);

        var baseDark = Color.FromRgb(0x1E, 0x1F, 0x22);
        Set("Accent", a);
        Set("AccentSoft", Alpha(a, dark ? 0.18 : 0.09));
        Set("AccentText", dark ? Lighten(a, 0.15) : Blend(a, Colors.Black, 0.18));
        Set("AccentLine", dark ? Blend(baseDark, Blend(a, b, 0.5), 0.45) : Blend(Colors.White, Blend(a, b, 0.5), 0.34));
        var flow = new LinearGradientBrush(a, b, 0);
        flow.Freeze();
        resources["Adawat.AccentFlow"] = flow;
        var flowDown = new LinearGradientBrush(a, b, 90);
        flowDown.Freeze();
        resources["Adawat.AccentFlowDown"] = flowDown;

        LinearGradientBrush Diagonal(Color from, Color to)
        {
            var brush = new LinearGradientBrush(from, to, new Point(0, 0), new Point(1, 1));
            brush.Freeze();
            return brush;
        }

        if (glass)
        {
            // Clear glass: only a light veil of color over the frosted backdrop, so what is behind
            // the window shows through. Clarity 1 is almost solid, 100 nearly clear.
            double veil = 0.62 - 0.5 * (_glassClarity - 1) / 99.0; // 0.62 .. 0.12
            var topColor = dark ? Blend(baseDark, a, 0.55) : Blend(Colors.White, a, 0.42);
            var bottomColor = dark ? Blend(baseDark, b, 0.45) : Blend(Colors.White, b, 0.34);
            resources["Adawat.Background"] = Diagonal(Alpha(topColor, veil + 0.06), Alpha(bottomColor, veil));
            // Text sits on see-through glass: chips and hovers are a little more solid so they read well.
            Set("AccentChip", dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
            Set("AccentChipHover", dark ? Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
            var sheen = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(dark ? (byte)0x1C : (byte)0x55, 0xFF, 0xFF, 0xFF), 0),
                new(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0.35),
            }, 90);
            sheen.Freeze();
            resources["Adawat.Sheen"] = sheen;
        }
        else
        {
            // Solid color: light enough to read on, the two colors flowing corner to corner.
            var topColor = dark ? Blend(baseDark, a, 0.30) : Blend(Colors.White, a, 0.24);
            var bottomColor = dark ? Blend(baseDark, b, 0.24) : Blend(Colors.White, b, 0.18);
            resources["Adawat.Background"] = Diagonal(topColor, bottomColor);
            resources["Adawat.Sheen"] = Brushes.Transparent;
        }
    }

    /// <summary>The palette color for a key (see <see cref="ApplyPalette"/>), for code that needs a raw color.</summary>
    public static Color PaletteColor(string key)
    {
        var resources = new ResourceDictionary();
        ApplyPalette(resources);
        return resources["Adawat." + key] is SolidColorBrush brush ? brush.Color : Colors.Gray;
    }

    /// <summary>A color of the translator's palette (see <see cref="ApplyTranslatorPalette"/>).</summary>
    public static Color TranslatorColor(string key)
    {
        var resources = new ResourceDictionary();
        ApplyTranslatorPalette(resources);
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
