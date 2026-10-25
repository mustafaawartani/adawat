using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Adawat.Core;

namespace Adawat.Translator;

public sealed record HistoryEntry(DateTime Time, string Source, string Translation, string SourceLanguage, string TargetLanguage);

/// <summary>Recent translations, saved to %AppData%\Adawat\history.json.</summary>
public sealed class HistoryStore
{
    private readonly TranslatorSettings _settings;
    private readonly List<HistoryEntry> _entries;

    public event Action? Changed;

    private static string FilePath => Path.Combine(AppPaths.DataFolder, "history.json");

    public HistoryStore(TranslatorSettings settings)
    {
        _settings = settings;
        _entries = Load();
    }

    public IReadOnlyList<HistoryEntry> Entries => _entries;

    public void Add(TranslationResult result)
    {
        if (!_settings.KeepHistory) return;
        var source = result.SourceText.Length > 4000 ? result.SourceText[..4000] : result.SourceText;
        var translation = result.Translation.Length > 4000 ? result.Translation[..4000] : result.Translation;
        if (_entries.Count > 0 && _entries[0].Source == source && _entries[0].TargetLanguage == result.TargetLanguage) return;

        _entries.Insert(0, new HistoryEntry(DateTime.Now, source, translation, result.SourceLanguage, result.TargetLanguage));
        int limit = Math.Clamp(_settings.HistoryLimit, 10, 5000);
        if (_entries.Count > limit) _entries.RemoveRange(limit, _entries.Count - limit);
        Save();
    }

    public void Clear()
    {
        _entries.Clear();
        Save();
    }

    private static List<HistoryEntry> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex)
        {
            Log.Error("Could not read history", ex);
        }
        return new();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_entries));
        }
        catch (Exception ex)
        {
            Log.Error("Could not save history", ex);
        }
        Changed?.Invoke();
    }

    public IEnumerable<HistoryEntry> Search(string query) =>
        string.IsNullOrWhiteSpace(query)
            ? _entries
            : _entries.Where(e => e.Source.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                                  || e.Translation.Contains(query, StringComparison.CurrentCultureIgnoreCase));
}
