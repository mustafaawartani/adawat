using System.Threading.Tasks;
using Adawat.Translator;

namespace Adawat.Core;

/// <summary>What the settings window needs from the running app.</summary>
public interface IAppHost
{
    AppSettings Settings { get; }
    HotkeyManager Hotkeys { get; }
    HistoryStore History { get; }
    bool TouchpadPresent { get; }

    /// <summary>Returns the screen zoom to normal size.</summary>
    void ResetZoom();

    /// <summary>Saves the settings and applies them right away.</summary>
    void SettingsChanged();

    /// <summary>Turns "run as administrator" on or off. Returns false if it could not be changed.</summary>
    bool SetRunElevated(bool elevated);

    void ShowHistoryEntry(HistoryEntry entry);

    Task<string> TestTranslationAsync();
}
