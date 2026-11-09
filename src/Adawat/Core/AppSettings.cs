using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Adawat.Core;

public enum ThemeMode { System, Light, Dark }

/// <summary>The color of buttons, links and highlights. "Windows" uses the Windows accent color.</summary>
public enum AccentChoice { Blue, Indigo, Violet, Teal, Green, Rose, Orange, Graphite, Windows }

public enum SelectionBehavior { ShowIcon, TranslateInstantly, DoNothing }

public enum SelectionModifier { None, Alt, Ctrl, Shift }

public enum CaptureMethod { Automatic, ClipboardOnly, AccessibilityOnly }

public enum SwipeAction { MouseButtons, AltArrows, BrowserKeys }

public enum PinchAction { ScreenZoom, CtrlWheel, CtrlPlusMinus }

/// <summary>How the translator window looks: frosted glass that lets what is behind show through, or solid color.</summary>
public enum TranslatorLook { Glass, Color }

/// <summary>
/// The translator's colors: the app's accent, one color, or two colors that flow into each other.
/// </summary>
public enum TranslatorColors
{
    AppAccent,
    Blue, Indigo, Violet, Teal, Green, Rose, Orange, Graphite,
    Ocean, Sunset, Aurora, Berry, Sky, Peach, Forest, Dusk,
}

/// <summary>Which side of the quick note box the tabs are on.</summary>
public enum TabSide { Left, Right }

/// <summary>Which Google Translate service translates.</summary>
public enum TranslationEngine
{
    /// <summary>The service behind translate.google.com itself (falls back to Classic when it fails).</summary>
    Website,
    /// <summary>The older free address used by browser extensions.</summary>
    Classic,
}

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; } = true;
    public bool RunElevated { get; set; }
    public ThemeMode Theme { get; set; } = ThemeMode.Light;
    public AccentChoice Accent { get; set; } = AccentChoice.Blue;
    public bool FirstRunCompleted { get; set; }
    /// <summary>Lets newer versions update settings saved by older ones.</summary>
    public int SettingsVersion { get; set; } = SettingsStore.CurrentVersion;

    public TranslatorSettings Translator { get; set; } = new();
    public OcrSettings Ocr { get; set; } = new();
    public GestureSettings Gestures { get; set; } = new();
    public ImageZoomSettings ImageZoom { get; set; } = new();
    public KeyRemapSettings KeyChanges { get; set; } = new();
    public KeepAwakeSettings KeepAwake { get; set; } = new();
    public QuickNoteSettings QuickNote { get; set; } = new();
}

public sealed class TranslatorSettings
{
    public bool Enabled { get; set; } = true;

    public string SourceLanguage { get; set; } = "auto";
    public string TargetLanguage { get; set; } = "ar";
    /// <summary>Optional extra language shown under the main translation ("" = off).</summary>
    public string SecondTargetLanguage { get; set; } = "";
    /// <summary>Used when the text is already in the target language.</summary>
    public string FallbackTargetLanguage { get; set; } = "en";
    public List<string> FavoriteLanguages { get; set; } = new() { "en", "es", "ar" };

    public string TranslateHotkey { get; set; } = "Alt+1";
    public string OpenBoxHotkey { get; set; } = "Alt+3";

    public SelectionBehavior OnSelection { get; set; } = SelectionBehavior.ShowIcon;
    public SelectionModifier SelectionModifier { get; set; } = SelectionModifier.None;
    public bool OnlyOverText { get; set; } = true;
    /// <summary>Keep the translate button until the selection goes away (click elsewhere, typing, scrolling, another app).</summary>
    public bool KeepIconWhileSelected { get; set; } = true;
    /// <summary>When <see cref="KeepIconWhileSelected"/> is off: hide the button after this many seconds.</summary>
    public double IconSeconds { get; set; } = 3;

    public double FontSize { get; set; } = 15;
    public double PopupWidth { get; set; } = 460;
    public bool CloseOnClickOutside { get; set; } = true;
    public bool ShowDictionary { get; set; } = true;
    public bool ShowTransliteration { get; set; } = true;
    public bool AutoCopyTranslation { get; set; }
    public bool ShowOpenInGoogle { get; set; } = true;

    public TranslatorLook Look { get; set; } = TranslatorLook.Glass;
    public TranslatorColors Colors { get; set; } = TranslatorColors.AppAccent;
    /// <summary>1..100: how see-through the glass is (higher shows more of what is behind the window).</summary>
    public int GlassClarity { get; set; } = 60;

    public TranslationEngine Engine { get; set; } = TranslationEngine.Website;
    /// <summary>
    /// Text copied from PDFs and narrow web columns has a line break at the end of every line, in the
    /// middle of sentences; Google then translates each line on its own. Join such lines first.
    /// </summary>
    public bool JoinBrokenLines { get; set; } = true;

    public CaptureMethod CaptureMethod { get; set; } = CaptureMethod.Automatic;
    public List<string> DisabledApps { get; set; } = new();
    /// <summary>Apps where Ctrl+C must never be sent (it stops programs in terminals).</summary>
    public List<string> NoCopyShortcutApps { get; set; } = new()
    {
        "WindowsTerminal.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "conhost.exe", "OpenConsole.exe",
        "wsl.exe", "mintty.exe", "alacritty.exe", "wezterm-gui.exe", "putty.exe", "kitty.exe", "Tabby.exe",
    };

    public bool KeepHistory { get; set; } = true;
    public int HistoryLimit { get; set; } = 300;
}

public sealed class OcrSettings
{
    public bool Enabled { get; set; } = true;
    public string Hotkey { get; set; } = "Alt+2";
    /// <summary>"auto" uses the Windows display languages, otherwise a language tag such as "en-US".</summary>
    public string Language { get; set; } = "auto";
    public bool TranslateAfterCapture { get; set; } = true;
    public bool CopyAfterCapture { get; set; }
    public bool KeepLineBreaks { get; set; } = true;
    public bool ImageContextMenu { get; set; } = true;
}

public sealed class GestureSettings
{
    public bool Enabled { get; set; } = true;

    public bool SwipeEnabled { get; set; } = true;
    public int SwipeFingers { get; set; } = 2;
    /// <summary>1..100, higher means a shorter swipe is enough.</summary>
    public int SwipeSensitivity { get; set; } = 50;
    public bool InvertSwipe { get; set; }
    public SwipeAction SwipeAction { get; set; } = SwipeAction.MouseButtons;
    public bool ShowSwipeArrow { get; set; } = true;
    /// <summary>Skip the swipe (no arrow, no back/forward) when the app's own back/forward button is greyed out.</summary>
    public bool SwipeOnlyWhenPossible { get; set; } = true;
    public List<string> SwipeDisabledApps { get; set; } = new()
    {
        "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "vivaldi.exe", "arc.exe", "zen.exe",
    };

    public bool PinchEnabled { get; set; } = true;
    public int PinchSensitivity { get; set; } = 50;
    public bool InvertPinch { get; set; }
    /// <summary>Screen zoom magnifies the picture like Chrome; the others make apps change their own zoom.</summary>
    public PinchAction PinchAction { get; set; } = PinchAction.ScreenZoom;
    public double MaxScreenZoom { get; set; } = 4;
    /// <summary>1..100: how fast the screen zooms as the fingers spread (see ZoomView.GainForSpeed).</summary>
    public int ScreenZoomSpeed { get; set; } = 50;
    /// <summary>Moving past the edge of the zoomed screen goes on to scroll the app, like Chrome.</summary>
    public bool ScrollPastZoomEdges { get; set; } = true;
    /// <summary>True: enlarged pixels are smoothed (like Windows Magnifier's "smooth edges"). False: blocky, crisp pixels.</summary>
    public bool SmoothZoomEdges { get; set; } = true;
    /// <summary>When the fingers lift near 2x, 3x…, settle exactly there (the sharpest picture).</summary>
    public bool SnapZoomLevels { get; set; }
    /// <summary>1..100: how far the zoomed view moves when panning with two fingers.</summary>
    public int PanSpeed { get; set; } = 40;
    /// <summary>False: the picture follows the fingers, like Chrome.</summary>
    public bool InvertPan { get; set; }
    /// <summary>
    /// True: pushing the mouse pointer against the edge of the zoomed view moves the view along.
    /// False: the pointer stops at the edge and only two fingers move the view.
    /// </summary>
    public bool PointerMovesZoom { get; set; }
    /// <summary>Set while Adawat has paused Windows' two-finger scrolling during a zoom.</summary>
    public bool ScrollPausedByAdawat { get; set; }
    /// <summary>
    /// Pause Windows' own pinch (Ctrl+wheel: icon size, font size) while the fingers are on the touchpad
    /// over an app where the screen zoom works, so the two do not fight. The Windows setting is never changed.
    /// </summary>
    public bool TurnOffWindowsPinch { get; set; } = true;
    /// <summary>Only read from settings of versions that switched Windows' pinch off for good, to switch it back on.</summary>
    public bool? WindowsPinchBefore { get; set; }
    /// <summary>Skip app zoom (Ctrl+wheel modes) when Windows already turns the pinch into zoom for the app.</summary>
    public bool SkipNativePinchApps { get; set; } = true;
    public List<string> PinchDisabledApps { get; set; } = new();

    public bool DisableInFullscreen { get; set; } = true;
}

/// <summary>Picture zoom: hover a picture anywhere to see it big, then save or copy it.</summary>
public sealed class ImageZoomSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>How long the pointer rests on a picture before it opens, in milliseconds.</summary>
    public int HoverDelayMs { get; set; } = 400;
    /// <summary>Only open while this key is held (None = always).</summary>
    public SelectionModifier Modifier { get; set; } = SelectionModifier.None;
    public string SaveHotkey { get; set; } = "Alt+S";
    public string CopyHotkey { get; set; } = "Alt+C";
    /// <summary>Pictures smaller than this (in screen pixels, both sides) are ignored, so icons don't pop up.</summary>
    public int MinSize { get; set; } = 32;
    /// <summary>Also enlarge pictures whose full-size file or address is unknown, from what is on screen.</summary>
    public bool EnlargeScreenPictures { get; set; } = true;
    public List<string> DisabledApps { get; set; } = new();
    public bool DisableInFullscreen { get; set; } = true;
}

/// <summary>Change keys: make one key act as another (e.g. Left Alt as Left Ctrl).</summary>
public sealed class KeyRemapSettings
{
    public bool Enabled { get; set; } = true;
    public List<KeyRemap> Remaps { get; set; } = new();
    /// <summary>Apps where keys keep their normal meaning (games, remote desktop…).</summary>
    public List<string> DisabledApps { get; set; } = new();
}

/// <summary>One key change. Keys are Windows virtual-key codes (left/right sides separate); To = 0 turns the key off.</summary>
public sealed class KeyRemap
{
    public int From { get; set; }
    public int To { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// Keys that, pressed while <see cref="From"/> is held, make it its old self again (e.g. Tab, so that
    /// with Left Alt working as Left Ctrl, Alt + Tab still switches windows).
    /// </summary>
    public List<int> Keep { get; set; } = new();

    /// <summary>The Alt shortcuts kept by default when Alt is made to work as Ctrl: Tab, F4, Esc, Space.</summary>
    public static List<int> DefaultAltKeep() => new() { 0x09, 0x73, 0x1B, 0x20 };
}

/// <summary>Stay awake: keep the PC working (also with the lid closed) for a while, then let it sleep again.</summary>
public sealed class KeepAwakeSettings
{
    /// <summary>How long, in minutes: 0 = until turned off, <see cref="CustomLength"/> = <see cref="CustomMinutes"/>, <see cref="UntilClockTime"/> = until <see cref="UntilClock"/>.</summary>
    public int Minutes { get; set; } = 60;
    public int CustomMinutes { get; set; } = 90;
    /// <summary>A time of day such as "18:30", for <see cref="UntilClockTime"/>.</summary>
    public string UntilClock { get; set; } = "18:00";

    public const int CustomLength = -1;
    public const int UntilClockTime = -2;
    /// <summary>Closing the lid does nothing while staying awake (Windows' lid setting is put back afterwards).</summary>
    public bool WhileLidClosed { get; set; } = true;
    /// <summary>Also keep the screen from turning off.</summary>
    public bool KeepScreenOn { get; set; }
    /// <summary>When the time is up with the lid closed, put the PC to sleep right away.</summary>
    public bool SleepWhenDoneIfLidClosed { get; set; } = true;

    /// <summary>Set while Adawat has changed the lid setting, so it can be put back even after a crash.</summary>
    public LidBackup? LidBackup { get; set; }
    /// <summary>When staying awake ends (UTC), null when off or until turned off. Kept so a restart of Adawat goes on.</summary>
    public DateTime? Until { get; set; }
    public bool Active { get; set; }
}

/// <summary>Quick note: a small box to jot notes down, saved automatically.</summary>
public sealed class QuickNoteSettings
{
    public bool Enabled { get; set; } = true;
    public string Hotkey { get; set; } = "Alt+4";
    public bool CloseOnClickOutside { get; set; } = true;
    /// <summary>Keep the box above other windows and open when clicking elsewhere (the pin in the box).</summary>
    public bool KeepOnTop { get; set; }
    public TabSide TabsSide { get; set; } = TabSide.Right;
    /// <summary>The color of the box; the same choices as the translator.</summary>
    public TranslatorColors Colors { get; set; } = TranslatorColors.AppAccent;
    public double FontSize { get; set; } = 15;
    public string FontFamily { get; set; } = "Segoe UI";
}

/// <summary>Windows' "when I close the lid" setting of one power plan, as it was before Adawat changed it.</summary>
public sealed class LidBackup
{
    public Guid Scheme { get; set; }
    public uint PluggedIn { get; set; }
    public uint OnBattery { get; set; }
}

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON in %AppData%\Adawat.</summary>
public static class SettingsStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public const int CurrentVersion = 6;

    public static string FilePath => Path.Combine(AppPaths.DataFolder, "settings.json");

    /// <summary>Moves settings saved by an older version to the current defaults where behavior changed.</summary>
    private static AppSettings Upgrade(AppSettings settings)
    {
        if (settings.SettingsVersion < 2)
        {
            // 0.1.1 zoomed apps with Ctrl+wheel; pinch now magnifies the screen like Chrome, everywhere.
            settings.Gestures.PinchAction = PinchAction.ScreenZoom;
            settings.Gestures.PinchDisabledApps = new();
            settings.Gestures.TurnOffWindowsPinch = true;
        }
        if (settings.SettingsVersion < 3)
        {
            // 0.1.x followed the Windows theme; the redesigned windows default to the light look.
            if (settings.Theme == ThemeMode.System) settings.Theme = ThemeMode.Light;
        }
        if (settings.SettingsVersion < 4)
        {
            // Blocky "sharp" pixels flickered while zooming; smooth edges look steadier and cleaner.
            settings.Gestures.SmoothZoomEdges = true;
            settings.Gestures.SnapZoomLevels = false;
        }
        if (settings.SettingsVersion < 5)
        {
            // The screen zoom speed used to share "pinch sensitivity" with app zoom, on a much narrower
            // range. Keep the same feel, except for anyone already at the slowest: they asked for slower.
            var g = settings.Gestures;
            double oldGain = 0.8 + 1.7 * Math.Clamp(g.PinchSensitivity, 1, 100) / 100.0;
            g.ScreenZoomSpeed = SpeedForGain(oldGain);
            if (g.PinchSensitivity <= 5) g.ScreenZoomSpeed = Math.Min(g.ScreenZoomSpeed, 30);
            // Small pictures such as profile pictures next to videos and comments now open too.
            if (settings.ImageZoom.MinSize == 48) settings.ImageZoom.MinSize = 32;
        }
        if (settings.SettingsVersion < 6)
        {
            // New: Alt + Tab, Alt + F4… can stay Alt shortcuts when Alt is made to work as Ctrl.
            foreach (var rule in settings.KeyChanges.Remaps)
            {
                if (rule.From is 0xA4 or 0xA5 && rule.To is 0xA2 or 0xA3 && rule.Keep.Count == 0) rule.Keep = KeyRemap.DefaultAltKeep();
            }
        }
        settings.SettingsVersion = CurrentVersion;
        return settings;
    }

    /// <summary>Same formula as ZoomView.SpeedForGain (kept here so the settings do not depend on the gestures code).</summary>
    private static int SpeedForGain(double gain) =>
        (int)Math.Round(Math.Clamp(100 * Math.Log(gain / 0.3) / Math.Log(2.5 / 0.3), 1, 100));

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                // Files written before versioning have no SettingsVersion and read as 0.
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
                    if (!json.Contains("\"SettingsVersion\"", StringComparison.OrdinalIgnoreCase)) settings.SettingsVersion = 0;
                    return Upgrade(settings);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not read settings, using defaults", ex);
            try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataFolder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
        }
    }
}
