using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Adawat.Core;
using Adawat.Interop;
using Adawat.Translator;
using Adawat.UI;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace Adawat.QuickNote;

/// <summary>
/// The quick note box: a text field with a narrow strip of tabs (one per note, tilted so they take
/// little width). Notes are saved a moment after typing stops, and when the box is hidden.
/// </summary>
public sealed class QuickNoteWindow : FluentWindow
{
    private readonly AppSettings _settings;
    private readonly NoteStore _store;
    private readonly Action _save;
    private readonly Action _settingsChanged;

    private readonly TextBox _editor;
    private readonly TextBlock _placeholder;
    private readonly Grid _body;
    private readonly Grid _editorHost;
    private readonly Border _strip;
    private readonly StackPanel _tabs = new();
    private readonly Border _pinButton;
    private readonly DispatcherTimer _saveTimer;
    private readonly Dictionary<string, TextBlock> _tabTitles = new();

    private Note? _current;
    private bool _quiet;       // suppresses change events while the box fills itself
    private int _menusOpen;    // a tab's right-click menu is open: clicking it must not close the box
    private IntPtr _hwnd;

    private QuickNoteSettings Q => _settings.QuickNote;

    public QuickNoteWindow(AppSettings settings, NoteStore store, Action save, Action settingsChanged)
    {
        _settings = settings;
        _store = store;
        _save = save;
        _settingsChanged = settingsChanged;

        Title = "Adawat Quick note";
        Icon = AppIcon.ImageSource;
        WindowBackdropType = WindowBackdropType.None;
        WindowCornerPreference = WindowCornerPreference.Round;
        ExtendsContentIntoTitleBar = true;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000;
        Top = -10000;
        // WPF UI's window style asks for a large minimum size; the box is meant to be small.
        MinWidth = 260;
        MinHeight = 180;
        Width = 380;
        Height = 320;
        FontFamily = new FontFamily(Ui.FontFamilyName);
        Background = Brushes.Transparent; // the colored layer is drawn by the content (see below)

        // ---- Header: a title to drag the box by, pin and close on the right --------------
        var title = PopupUi.Text("Quick note", 12, "Muted", FontWeights.SemiBold);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.TextWrapping = TextWrapping.NoWrap;
        title.Margin = new Thickness(6, 0, 0, 0);
        _pinButton = PopupUi.IconButton(SymbolRegular.Pin20, "Keep on top", TogglePin);
        var close = PopupUi.IconButton(SymbolRegular.Dismiss20, "Close (Esc)", HideWindow);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(_pinButton);
        tools.Children.Add(close);
        DockPanel.SetDock(tools, Dock.Right);
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(10, 8, 8, 0), Background = Brushes.Transparent };
        header.Children.Add(tools);
        header.Children.Add(title);
        header.MouseLeftButtonDown += (_, _) =>
        {
            try { DragMove(); }
            catch (InvalidOperationException) { } // the button was already released
        };
        WindowChrome.SetIsHitTestVisibleInChrome(tools, true);

        // ---- The note: no box, just the text ---------------------------------------------
        _editor = PopupUi.PlainTextBox(Q.FontSize, readOnly: false);
        _editor.AcceptsReturn = true;
        _editor.VerticalAlignment = VerticalAlignment.Stretch;
        _placeholder = PopupUi.Text("Type a note…", Q.FontSize, "Faint");
        _placeholder.IsHitTestVisible = false;
        _placeholder.VerticalAlignment = VerticalAlignment.Top;
        _editorHost = new Grid { Background = Brushes.Transparent, Cursor = Cursors.IBeam, Margin = new Thickness(14, 6, 14, 12) };
        _editorHost.Children.Add(_placeholder);
        _editorHost.Children.Add(_editor);
        _editorHost.MouseLeftButtonDown += (_, _) => _editor.Focus();
        _editor.TextChanged += (_, _) => OnTextChanged();

        // ---- The strip of tabs, "+" on top ----------------------------------------------------
        var add = PopupUi.IconButton(SymbolRegular.Add20, "New note (Ctrl+N)", NewNote, 16, brush: "AccentText");
        add.HorizontalAlignment = HorizontalAlignment.Center;
        add.Margin = new Thickness(0, 0, 0, 4);
        var stripContent = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(add, Dock.Top);
        stripContent.Children.Add(add);
        stripContent.Children.Add(new ScrollViewer
        {
            Content = _tabs,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        });
        _strip = new Border { Width = 38, Padding = new Thickness(0, 2, 0, 8), Child = stripContent };
        _strip.SetResourceReference(Border.BorderBrushProperty, "Adawat.AccentLine");

        _body = new Grid();
        _body.Children.Add(_editorHost);
        _body.Children.Add(_strip);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(header, 0);
        Grid.SetRow(_body, 1);
        root.Children.Add(header);
        root.Children.Add(_body);
        var tint = new Border { Child = root };
        tint.SetResourceReference(Border.BackgroundProperty, "Adawat.Background");
        Content = tint;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => Flush();

        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) =>
        {
            if (IsVisible && !Q.KeepOnTop && Q.CloseOnClickOutside && _menusOpen == 0) HideWindow();
        };
        Closing += (_, e) =>
        {
            // Keep the window for next time; it opens much faster than a new one.
            e.Cancel = true;
            HideWindow();
        };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyBorderColor();
        };
        ThemeHelper.Changed += ApplySettings;

        ApplySettings();
    }

    // ---- Look ----------------------------------------------------------------------------

    /// <summary>Applies the settings (colors, font, tab side, pin) to the box, which may be open.</summary>
    public void ApplySettings()
    {
        ThemeHelper.ApplyNotePalette(Resources, Q.Colors);
        ApplyBorderColor();

        Topmost = Q.KeepOnTop;
        PopupUi.SetActive(_pinButton, Q.KeepOnTop);
        _pinButton.ToolTip = Q.KeepOnTop ? "Stop keeping on top" : "Keep on top";

        double size = Math.Clamp(Q.FontSize, 10, 40);
        _editor.FontSize = size;
        _placeholder.FontSize = size;
        _editor.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(Q.FontFamily) ? "Segoe UI" : Q.FontFamily);

        bool right = Q.TabsSide == TabSide.Right;
        _body.ColumnDefinitions.Clear();
        _body.ColumnDefinitions.Add(new ColumnDefinition { Width = right ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        _body.ColumnDefinitions.Add(new ColumnDefinition { Width = right ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_editorHost, right ? 0 : 1);
        Grid.SetColumn(_strip, right ? 1 : 0);
        _strip.BorderThickness = right ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0);
    }

    /// <summary>Windows 11 draws a thin border around the window; give it the palette's border color.</summary>
    private void ApplyBorderColor()
    {
        if (_hwnd == IntPtr.Zero) return;
        var c = Resources["Adawat.AccentLine"] is SolidColorBrush brush ? brush.Color : Colors.Gray;
        int colorRef = c.R | (c.G << 8) | (c.B << 16);
        Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));
    }

    private void TogglePin()
    {
        Q.KeepOnTop = !Q.KeepOnTop;
        ApplySettings();
        _settingsChanged();
    }

    // ---- Showing and hiding ----------------------------------------------------------------

    /// <summary>Shows the box with the last open note, ready to type.</summary>
    public void ShowNote()
    {
        ApplySettings();
        if (!IsVisible)
        {
            PlaceWindow();
            var note = _store.Find(_store.Data.LastOpenId) ?? _store.Notes.FirstOrDefault();
            if (note != null) SelectNote(note.Id, focus: false);
            else NewNote();
            Show();
        }
        Activate();
        if (_hwnd != IntPtr.Zero) Native.SetForegroundWindow(_hwnd);
        FocusEditor();
    }

    public void HideWindow()
    {
        if (!IsVisible) return;
        _store.PruneEmpty();
        _current = null;
        Flush();
        Hide();
    }

    /// <summary>Saves the notes now (and the position and size of the box).</summary>
    public void Flush()
    {
        _saveTimer.Stop();
        CaptureGeometry();
        _save();
    }

    private void CaptureGeometry()
    {
        if (!IsVisible || WindowState != WindowState.Normal) return;
        var d = _store.Data;
        d.Left = Left;
        d.Top = Top;
        d.Width = ActualWidth;
        d.Height = ActualHeight;
    }

    private void PlaceWindow()
    {
        var d = _store.Data;
        double w = Math.Clamp(d.Width ?? 380, MinWidth, 1400);
        double h = Math.Clamp(d.Height ?? 320, MinHeight, 1400);
        Width = w;
        Height = h;

        double left, top;
        if (d.Left is { } l && d.Top is { } t && IsOnScreen(l, t, w))
        {
            left = l;
            top = t;
        }
        else
        {
            var work = SystemParameters.WorkArea;
            left = work.Left + (work.Width - w) / 2;
            top = work.Top + (work.Height - h) / 2;
        }
        Left = left;
        Top = top;
    }

    /// <summary>True if enough of the box would be on some screen to grab it (screens may have changed).</summary>
    private static bool IsOnScreen(double left, double top, double width)
    {
        double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
        double vr = vl + SystemParameters.VirtualScreenWidth, vb = vt + SystemParameters.VirtualScreenHeight;
        return left + width > vl + 100 && left < vr - 100 && top >= vt - 10 && top < vb - 60;
    }

    private void FocusEditor() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            _editor.Focus();
            Keyboard.Focus(_editor);
            _editor.CaretIndex = _editor.Text.Length;
            _editor.ScrollToEnd();
        }));

    // ---- Keys -------------------------------------------------------------------------------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideWindow();
        }
        else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            NewNote();
        }
        else if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            HideWindow();
        }
    }

    // ---- Notes ------------------------------------------------------------------------------

    private void OnTextChanged()
    {
        _placeholder.Visibility = _editor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateDirection();
        if (_quiet || _current == null) return;
        _store.Update(_current.Id, _editor.Text, DateTime.Now);
        UpdateTabTitle(_current);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Arabic and other right-to-left notes start from the right.</summary>
    private void UpdateDirection()
    {
        var direction = Languages.LooksRightToLeft(_editor.Text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        if (_editor.FlowDirection != direction) _editor.FlowDirection = direction;
    }

    private void NewNote()
    {
        var note = _store.NewNote(DateTime.Now);
        SelectNote(note.Id);
    }

    private void SelectNote(string id, bool focus = true)
    {
        var note = _store.Find(id);
        if (note == null) return;
        string? previous = _current?.Id;
        _current = note;
        _store.Data.LastOpenId = id;
        // Leaving an empty note drops it; empty notes are not kept.
        if (previous != null && previous != id) _store.PruneEmpty(keepId: id);

        _quiet = true;
        _editor.IsUndoEnabled = false; // Ctrl+Z must not bring back the text of another note
        _editor.Text = note.Text;
        _editor.IsUndoEnabled = true;
        _quiet = false;
        _placeholder.Visibility = note.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateDirection();

        RebuildTabs();
        if (focus) FocusEditor();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void DeleteNote(string id)
    {
        bool wasCurrent = _current?.Id == id;
        if (!_store.Delete(id, out var next)) return;
        if (wasCurrent)
        {
            _current = null;
            if (next != null) SelectNote(next);
            else NewNote();
        }
        else
        {
            RebuildTabs();
        }
        Flush();
    }

    // ---- Tabs -------------------------------------------------------------------------------

    private void RebuildTabs()
    {
        _tabs.Children.Clear();
        _tabTitles.Clear();
        foreach (var note in _store.Notes) _tabs.Children.Add(BuildTab(note, note.Id == _current?.Id));
    }

    private Border BuildTab(Note note, bool selected)
    {
        var label = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 120, // the length of the tab: this is its width before it is tilted
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            LayoutTransform = new RotateTransform(-90),
            FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, selected ? "Adawat.AccentText" : "Adawat.Muted");
        _tabTitles[note.Id] = label;

        var tab = new Border
        {
            Width = 30,
            MinHeight = 44,
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(0, 8, 0, 8),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = label,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        string id = note.Id;
        PopupUi.MakeClickable(tab, () => SelectNote(id), hoverBrush: "Hover");
        PopupUi.SetActive(tab, selected);
        SetTabTitle(label, tab, note);

        var menu = new System.Windows.Controls.ContextMenu();
        var delete = new System.Windows.Controls.MenuItem { Header = "Delete" };
        delete.Click += (_, _) => DeleteNote(id);
        menu.Items.Add(delete);
        menu.Opened += (_, _) => _menusOpen++;
        menu.Closed += (_, _) => _menusOpen = Math.Max(0, _menusOpen - 1);
        tab.ContextMenu = menu;
        return tab;
    }

    private void UpdateTabTitle(Note note)
    {
        if (_tabTitles.TryGetValue(note.Id, out var label) && label.Parent is Border tab) SetTabTitle(label, tab, note);
    }

    private static void SetTabTitle(TextBlock label, Border tab, Note note)
    {
        var title = NoteStore.TitleOf(note.Text);
        bool empty = title.Length == 0;
        label.Text = empty ? "New note" : title;
        label.FontStyle = empty ? FontStyles.Italic : FontStyles.Normal;
        label.FlowDirection = Languages.LooksRightToLeft(title) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var full = note.Text.Trim();
        tab.ToolTip = empty ? "New note" : full.Length > 300 ? full[..300] + "…" : full;
    }
}
