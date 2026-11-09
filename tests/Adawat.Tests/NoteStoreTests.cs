using Adawat.QuickNote;
using Xunit;

namespace Adawat.Tests;

public class NoteStoreTests
{
    private static readonly DateTime T0 = new(2025, 1, 1, 10, 0, 0);

    private static Note Add(NoteStore store, string text, DateTime created)
    {
        var note = store.NewNote(created);
        store.Update(note.Id, text, created);
        return note;
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   \n  ", "")]
    [InlineData("Buy milk", "Buy milk")]
    [InlineData("  Buy   milk\tand eggs  ", "Buy milk and eggs")]
    [InlineData("\n\nSecond line wins\nthird", "Second line wins")]
    [InlineData("first\r\nsecond", "first")]
    public void TitleIsTheFirstLine(string text, string expected) => Assert.Equal(expected, NoteStore.TitleOf(text));

    [Fact]
    public void LongTitleIsCutAtAWord()
    {
        var title = NoteStore.TitleOf("Remember to call the dentist tomorrow morning", 24);
        Assert.Equal("Remember to call the…", title);
    }

    [Fact]
    public void LongTitleWithoutSpacesIsCutHard()
    {
        var title = NoteStore.TitleOf(new string('x', 50), 10);
        Assert.Equal(new string('x', 10) + "…", title);
    }

    [Fact]
    public void ArabicTitleWorks()
    {
        Assert.Equal("مرحبا بالعالم", NoteStore.TitleOf("  مرحبا   بالعالم\nسطر ثان"));
    }

    [Fact]
    public void TitleDoesNotSplitASurrogatePair()
    {
        var title = NoteStore.TitleOf("ab" + "😀😀😀😀", 3);
        Assert.Equal("ab…", title);
    }

    [Fact]
    public void NewNotesGoFirst()
    {
        var store = new NoteStore();
        var a = Add(store, "a", T0);
        var b = Add(store, "b", T0.AddMinutes(1));
        Assert.Equal(new[] { b.Id, a.Id }, store.Notes.Select(n => n.Id).ToArray());
    }

    [Fact]
    public void NewNoteReusesTheEmptyOne()
    {
        var store = new NoteStore();
        var first = store.NewNote(T0);
        var second = store.NewNote(T0.AddMinutes(1));
        Assert.Same(first, second);
        Assert.Single(store.Notes);
    }

    [Fact]
    public void UpdateChangesTextAndTime()
    {
        var store = new NoteStore();
        var note = Add(store, "old", T0);
        Assert.True(store.Update(note.Id, "new", T0.AddHours(1)));
        Assert.Equal("new", note.Text);
        Assert.Equal(T0.AddHours(1), note.Modified);
        Assert.Equal(T0, note.Created);
        Assert.False(store.Update("missing", "x", T0));
    }

    [Fact]
    public void DeleteTellsWhichNoteComesNext()
    {
        var store = new NoteStore();
        var a = Add(store, "a", T0);
        var b = Add(store, "b", T0.AddMinutes(1));
        var c = Add(store, "c", T0.AddMinutes(2)); // order: c, b, a

        Assert.True(store.Delete(b.Id, out var next));
        Assert.Equal(a.Id, next);
        Assert.True(store.Delete(a.Id, out next));
        Assert.Equal(c.Id, next);
        Assert.True(store.Delete(c.Id, out next));
        Assert.Null(next);
        Assert.Empty(store.Notes);
        Assert.False(store.Delete(c.Id, out _));
    }

    [Fact]
    public void PruneRemovesEmptyNotesExceptTheKept()
    {
        var store = new NoteStore();
        Add(store, "keep me", T0);
        var empty = store.NewNote(T0.AddMinutes(1));
        Assert.Equal(0, store.PruneEmpty(keepId: empty.Id));
        Assert.Equal(2, store.Notes.Count);
        Assert.Equal(1, store.PruneEmpty());
        Assert.Single(store.Notes);
        Assert.Equal("keep me", store.Notes[0].Text);
    }

    [Fact]
    public void WhitespaceOnlyCountsAsEmpty()
    {
        var store = new NoteStore();
        Add(store, " \n\t ", T0);
        Assert.Equal(1, store.PruneEmpty());
    }

    [Fact]
    public void JsonRoundTrip()
    {
        var store = new NoteStore();
        Add(store, "first", T0);
        var second = Add(store, "مرحبا\nline two", T0.AddMinutes(5));
        store.Data.LastOpenId = second.Id;
        store.Data.Left = 120.5;
        store.Data.Top = 80;
        store.Data.Width = 400;
        store.Data.Height = 300;

        var loaded = NoteStore.FromJson(store.ToJson());

        Assert.Equal(store.Notes.Select(n => (n.Id, n.Text, n.Created, n.Modified)),
                     loaded.Notes.Select(n => (n.Id, n.Text, n.Created, n.Modified)));
        Assert.Equal(second.Id, loaded.Data.LastOpenId);
        Assert.Equal(120.5, loaded.Data.Left);
        Assert.Equal(80.0, loaded.Data.Top);
        Assert.Equal(400.0, loaded.Data.Width);
        Assert.Equal(300.0, loaded.Data.Height);
    }

    [Fact]
    public void EmptyNotesAreNotSaved()
    {
        var store = new NoteStore();
        Add(store, "real", T0);
        var empty = store.NewNote(T0.AddMinutes(1));
        store.Data.LastOpenId = empty.Id;

        var loaded = NoteStore.FromJson(store.ToJson());

        Assert.Single(loaded.Notes);
        Assert.Equal("real", loaded.Notes[0].Text);
        Assert.Null(loaded.Data.LastOpenId);
        Assert.Equal(2, store.Notes.Count); // the open (empty) note stays in memory
    }

    [Fact]
    public void LoadingSortsNewestFirstAndFixesIds()
    {
        var json = """
            { "Notes": [
                { "Id": "a", "Text": "old", "Created": "2025-01-01T10:00:00" },
                { "Id": "a", "Text": "same id", "Created": "2025-01-03T10:00:00" },
                { "Id": "", "Text": "no id", "Created": "2025-01-02T10:00:00" },
                { "Id": "z", "Text": "", "Created": "2025-01-04T10:00:00" }
            ], "LastOpenId": "z" }
            """;
        var store = NoteStore.FromJson(json);

        Assert.Equal(new[] { "same id", "no id", "old" }, store.Notes.Select(n => n.Text).ToArray());
        Assert.Equal(3, store.Notes.Select(n => n.Id).Distinct().Count());
        Assert.All(store.Notes, n => Assert.False(string.IsNullOrEmpty(n.Id)));
        Assert.Null(store.Data.LastOpenId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json {")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    [InlineData("{\"Notes\": null}")]
    public void BadFilesGiveAnEmptyStore(string? json) => Assert.Empty(NoteStore.FromJson(json).Notes);

    [Fact]
    public void OldFilesWithMissingFieldsStillLoad()
    {
        var store = NoteStore.FromJson("{\"Notes\":[{\"Id\":\"1\",\"Text\":\"hi\"}]}");
        Assert.Single(store.Notes);
        Assert.Null(store.Data.Left);
    }

    [Fact]
    public void SaveAndLoadFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "adawat-notes-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "notes.json");
        try
        {
            var store = new NoteStore();
            Add(store, "on disk", T0);
            store.Save(path);

            var loaded = NoteStore.Load(path);
            Assert.Equal("on disk", Assert.Single(loaded.Notes).Text);
            Assert.False(File.Exists(path + ".tmp"));
            Assert.Empty(NoteStore.Load(Path.Combine(folder, "missing.json")).Notes);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void DamagedFileIsKeptAsBroken()
    {
        var folder = Path.Combine(Path.GetTempPath(), "adawat-notes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "notes.json");
        try
        {
            File.WriteAllText(path, "{ broken");
            Assert.Empty(NoteStore.Load(path).Notes);
            Assert.True(File.Exists(path + ".broken"));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
