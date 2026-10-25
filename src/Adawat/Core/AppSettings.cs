using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Adawat.Core;

public enum ThemeMode { System, Light, Dark }

public enum SelectionBehavior { ShowIcon, TranslateInstantly, DoNothing }

public enum SelectionModifier { None, Alt, Ctrl, Shift }

public enum CaptureMethod { Automatic, ClipboardOnly, AccessibilityOnly }

public enum SwipeAction { MouseButtons, AltArrows, BrowserKeys }

public enum PinchAction { ScreenZoom, CtrlWheel, CtrlPlusMinus }

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; } = true;
    public bool RunElevated { get; set; }
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public bool FirstRunCompleted { get; set; }
    /// <summary>Lets newer versions update settings saved by older ones.</summary>
    public int SettingsVersion { get; set; } = SettingsStore.CurrentVersion;

    public TranslatorSettings Translator { get; set; } = new();
    public OcrSettings Ocr { get; set; } = new();
    public GestureSettings Gestures { get; set; } = new();
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
    public double IconSeconds { get; set; } = 3;

    public double FontSize { get; set; } = 15;
    public double PopupWidth { get; set; } = 460;
    public bool CloseOnClickOutside { get; set; } = true;
    public bool ShowDictionary { get; set; } = true;
    public bool ShowTransliteration { get; set; } = true;
    public bool AutoCopyTranslation { get; set; }

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
    public List<string> SwipeDisabledApps { get; set; } = new()
    {
        "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "vivaldi.exe", "arc.exe", "zen.exe",
    };

    public bool PinchEnabled { get; set; } = true;
    public int PinchSensitivity { get; set; } = 50;
    public bool InvertPinch { get; set; }
    /// <summary>Screen zoom magnifies the picture like Chrome; the others make apps change their own zoom.</summary>
    public PinchAction PinchAction { get; set; } = PinchAction.ScreenZoom;
    public double MaxScreenZoom { get; set; } = 5;
    /// <summary>Turn off Windows' own pinch (Ctrl+wheel: icon size, font size) so it does not fight the screen zoom.</summary>
    public bool TurnOffWindowsPinch { get; set; } = true;
    /// <summary>What Windows' pinch setting was before Adawat turned it off, to put it back later.</summary>
    public bool? WindowsPinchBefore { get; set; }
    /// <summary>Skip app zoom (Ctrl+wheel modes) when Windows already turns the pinch into zoom for the app.</summary>
    public bool SkipNativePinchApps { get; set; } = true;
    public List<string> PinchDisabledApps { get; set; } = new();

    public bool DisableInFullscreen { get; set; } = true;
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

    public const int CurrentVersion = 2;

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
        settings.SettingsVersion = CurrentVersion;
        return settings;
    }

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
