using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Adawat.QuickNote;

/// <summary>One saved note.</summary>
public sealed class Note
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
}

/// <summary>What is saved in notes.json: the notes plus where the window was and which note was open.</summary>
public sealed class NoteData
{
    public List<Note> Notes { get; set; } = new();
    public string? LastOpenId { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
}

/// <summary>
/// The notes of the quick note tool. Pure logic (no windows, no Adawat services), so it can be tested:
/// newest-first order, titles, at most one empty note, pruning empty notes and the JSON file.
/// </summary>
public sealed class NoteStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public NoteData Data { get; }

    /// <summary>The notes, newest first.</summary>
    public IReadOnlyList<Note> Notes => Data.Notes;

    public NoteStore(NoteData? data = null)
    {
        Data = data ?? new NoteData();
        Normalize();
    }

    public static bool IsEmpty(string? text) => string.IsNullOrWhiteSpace(text);

    /// <summary>
    /// The short name of a note for its tab: the first words of its first line, cut at
    /// <paramref name="maxLength"/> characters (with "…"). Empty for an empty note.
    /// </summary>
    public static string TitleOf(string? text, int maxLength = 24)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        maxLength = Math.Max(1, maxLength);
        string line = "";
        foreach (var candidate in text.Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                line = candidate;
                break;
            }
        }
        // Collapse tabs and runs of spaces into single spaces.
        var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var title = string.Join(" ", words);
        if (title.Length <= maxLength) return title;

        var cut = title[..maxLength];
        int space = cut.LastIndexOf(' ');
        if (space >= maxLength / 2) cut = cut[..space];
        if (cut.Length > 0 && char.IsHighSurrogate(cut[^1])) cut = cut[..^1];
        return cut.TrimEnd() + "…";
    }

    public Note? Find(string? id) =>
        id == null ? null : Data.Notes.FirstOrDefault(n => n.Id == id);

    /// <summary>
    /// Starts a new note at the top of the list. If there already is an empty note, that one is
    /// returned instead, so pressing "+" again and again does not pile up empty notes.
    /// </summary>
    public Note NewNote(DateTime now)
    {
        var empty = Data.Notes.FirstOrDefault(n => IsEmpty(n.Text));
        if (empty != null) return empty;
        var note = new Note { Id = Guid.NewGuid().ToString("N"), Text = "", Created = now, Modified = now };
        Data.Notes.Insert(0, note);
        return note;
    }

    /// <summary>Replaces the text of a note. Returns false if there is no such note.</summary>
    public bool Update(string id, string text, DateTime now)
    {
        var note = Find(id);
        if (note == null) return false;
        if (note.Text == text) return true;
        note.Text = text;
        note.Modified = now;
        return true;
    }

    /// <summary>
    /// Removes a note. <paramref name="nextId"/> is the note to show instead (the one now in its place, or
    /// the last one), null when no notes are left. Returns false if there was no such note.
    /// </summary>
    public bool Delete(string id, out string? nextId)
    {
        nextId = null;
        int index = Data.Notes.FindIndex(n => n.Id == id);
        if (index < 0) return false;
        Data.Notes.RemoveAt(index);
        if (Data.LastOpenId == id) Data.LastOpenId = null;
        if (Data.Notes.Count > 0) nextId = Data.Notes[Math.Min(index, Data.Notes.Count - 1)].Id;
        return true;
    }

    /// <summary>Removes the empty notes, except <paramref name="keepId"/>. Returns how many were removed.</summary>
    public int PruneEmpty(string? keepId = null)
    {
        int removed = Data.Notes.RemoveAll(n => IsEmpty(n.Text) && n.Id != keepId);
        if (Data.LastOpenId != null && Find(Data.LastOpenId) == null) Data.LastOpenId = null;
        return removed;
    }

    /// <summary>Fixes what an edited or damaged file can contain: missing or repeated ids, empty notes, the order.</summary>
    private void Normalize()
    {
        Data.Notes ??= new List<Note>();
        var seen = new HashSet<string>();
        var kept = new List<Note>();
        foreach (var note in Data.Notes)
        {
            if (note == null) continue;
            note.Text ??= "";
            if (IsEmpty(note.Text)) continue;
            if (string.IsNullOrEmpty(note.Id) || !seen.Add(note.Id))
            {
                note.Id = Guid.NewGuid().ToString("N");
                seen.Add(note.Id);
            }
            kept.Add(note);
        }
        // OrderBy is stable: notes created at the same moment keep their order.
        Data.Notes = kept.OrderByDescending(n => n.Created).ToList();
        if (Data.LastOpenId != null && Find(Data.LastOpenId) == null) Data.LastOpenId = null;
    }

    // ---- JSON ----------------------------------------------------------------------------------

    /// <summary>The file contents: empty notes are left out.</summary>
    public string ToJson()
    {
        var notes = Data.Notes.Where(n => !IsEmpty(n.Text)).ToList();
        var copy = new NoteData
        {
            Notes = notes,
            LastOpenId = notes.Any(n => n.Id == Data.LastOpenId) ? Data.LastOpenId : null,
            Left = Data.Left,
            Top = Data.Top,
            Width = Data.Width,
            Height = Data.Height,
        };
        return JsonSerializer.Serialize(copy, JsonOptions);
    }

    /// <summary>Reads file contents; anything unreadable gives an empty store.</summary>
    public static NoteStore FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new NoteStore();
        try
        {
            return new NoteStore(JsonSerializer.Deserialize<NoteData>(json, JsonOptions));
        }
        catch (JsonException)
        {
            return new NoteStore();
        }
    }

    /// <summary>Loads the file; a missing file gives an empty store, a damaged one is kept as ".broken".</summary>
    public static NoteStore Load(string path)
    {
        string json;
        try
        {
            if (!File.Exists(path)) return new NoteStore();
            json = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return new NoteStore();
        }
        catch (UnauthorizedAccessException)
        {
            return new NoteStore();
        }

        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                return new NoteStore(JsonSerializer.Deserialize<NoteData>(json, JsonOptions));
            }
            catch (JsonException)
            {
                try { File.Copy(path, path + ".broken", true); } catch (Exception) { }
            }
        }
        return new NoteStore();
    }

    /// <summary>Writes the file (to a temporary file first, so a crash cannot leave half a file).</summary>
    public void Save(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ToJson());
        File.Move(tmp, path, true);
    }
}
