using System;
using System.IO;
using Adawat.Core;

namespace Adawat.QuickNote;

/// <summary>
/// Quick note: a shortcut opens a small box to type a note into; press it again to hide the box.
/// Notes are saved to notes.json in the Adawat data folder. The notes and the window are created
/// the first time the box is opened.
/// </summary>
public sealed class QuickNoteService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Action _settingsChanged;
    private NoteStore? _store;
    private QuickNoteWindow? _window;

    public static string NotesFile => Path.Combine(AppPaths.DataFolder, "notes.json");

    public QuickNoteService(AppSettings settings, Action settingsChanged)
    {
        _settings = settings;
        _settingsChanged = settingsChanged;
    }

    private NoteStore Store => _store ??= NoteStore.Load(NotesFile);

    private QuickNoteWindow Window => _window ??= new QuickNoteWindow(_settings, Store, Save, _settingsChanged);

    /// <summary>The shortcut: hides the box when it is open, otherwise opens it.</summary>
    public void Toggle()
    {
        if (_window is { IsVisible: true }) _window.HideWindow();
        else Show();
    }

    public void Show()
    {
        try
        {
            Window.ShowNote();
        }
        catch (Exception ex)
        {
            Log.Error("Could not open the quick note", ex);
        }
    }

    /// <summary>Applies changed settings to the box (it may be open).</summary>
    public void Apply()
    {
        if (_window == null) return;
        if (!_settings.QuickNote.Enabled && _window.IsVisible) _window.HideWindow();
        _window.ApplySettings();
    }

    private void Save()
    {
        try
        {
            _store?.Save(NotesFile);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save the notes", ex);
        }
    }

    /// <summary>Saves everything (when Adawat closes).</summary>
    public void Dispose() => _window?.Flush();
}
