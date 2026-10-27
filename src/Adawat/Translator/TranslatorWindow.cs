using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Adawat.Core;
using Adawat.Interop;
using Adawat.UI;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace Adawat.Translator;

public enum PopupMode { Selection, Manual, Image }

/// <summary>The translation popup: language pickers, the text, the translation, dictionary and voice.</summary>
public sealed class TranslatorWindow : FluentWindow
{
    private readonly AppSettings _settings;
    private readonly GoogleTranslator _translator;
    private readonly SpeechPlayer _speech;
    private readonly HistoryStore _history;
    private readonly Action _openSettings;
    private readonly Action _saveSettings;

    private readonly LanguagePicker _sourcePicker = new();
    private readonly LanguagePicker _targetPicker = new();
    private readonly TextBox _sourceBox;
    private readonly TextBlock _placeholder;
    private readonly Border _bottom;
    private readonly TextBox _resultBox;
    private readonly TextBox _secondBox;
    private readonly TextBlock _detected;
    private readonly TextBlock _translit;
    private readonly TextBlock _secondLabel;
    private readonly TextBlock _status;
    private readonly TextBlock _modeLabel;
    private readonly TextBlock _googleLink;
    private readonly DockPanel _footer;
    private readonly StackPanel _dictionaryPanel = new();
    private readonly Border _resultCard;
    private readonly Border _secondCard;
    private readonly Border _pinButton;
    private readonly DispatcherTimer _typingTimer;

    private CancellationTokenSource? _cts;
    private TranslationResult? _last;
    private TranslationResult? _pendingHistory;
    private PopupMode _mode;
    private Native.POINT _anchor;
    private bool _pinned;
    private bool _statusIsError;
    private bool _quiet; // suppresses change events while the window fills itself
    private IntPtr _hwnd;

    private TranslatorSettings T => _settings.Translator;

    public TranslatorWindow(AppSettings settings, GoogleTranslator translator, SpeechPlayer speech, HistoryStore history,
        Action openSettings, Action saveSettings)
    {
        _settings = settings;
        _translator = translator;
        _speech = speech;
        _history = history;
        _openSettings = openSettings;
        _saveSettings = saveSettings;

        Title = "Adawat Translator";
        Icon = AppIcon.ImageSource;
        // A plain, solid surface: calmer and easier to read than the see-through acrylic.
        WindowBackdropType = WindowBackdropType.None;
        WindowCornerPreference = WindowCornerPreference.Round;
        ExtendsContentIntoTitleBar = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        // Not SizeToContent: with the extended title bar WPF adds a blank strip at the bottom.
        // The height follows the content instead (see the scroller below).
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000;
        Top = -10000;
        MaxHeight = 760;
        FontFamily = new FontFamily(Ui.FontFamilyName);
        ApplyTheme();
        SetResourceReference(BackgroundProperty, "Adawat.Surface");

        // ---- Header: languages in a capsule, window buttons on the right ----------------
        var swap = PopupUi.IconButton(SymbolRegular.ArrowSwap20, "Swap languages", Swap, 15);
        swap.Width = 28;
        swap.Height = 28;
        _targetPicker.Emphasized = true;
        _pinButton = PopupUi.IconButton(SymbolRegular.Pin20, "Keep open", TogglePin);
        var languages = new StackPanel { Orientation = Orientation.Horizontal };
        languages.Children.Add(_sourcePicker);
        languages.Children.Add(swap);
        languages.Children.Add(_targetPicker);
        var capsule = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(3), Child = languages };
        capsule.SetResourceReference(Border.BackgroundProperty, "Adawat.Subtle");
        var header = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, -4, 12), Background = Brushes.Transparent };
        DockPanel.SetDock(capsule, Dock.Left);
        header.Children.Add(capsule);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(_pinButton);
        tools.Children.Add(PopupUi.IconButton(SymbolRegular.Settings20, "Settings", () => { HideWindow(); _openSettings(); }));
        tools.Children.Add(PopupUi.IconButton(SymbolRegular.Dismiss20, "Close (Esc)", HideWindow));
        DockPanel.SetDock(tools, Dock.Right);
        header.Children.Add(tools);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DockPanel) DragMove();
        };
        foreach (UIElement child in new UIElement[] { capsule, tools }) WindowChrome.SetIsHitTestVisibleInChrome(child, true);

        // ---- Source text: no box, just the text ---------------------------------------
        _sourceBox = PopupUi.PlainTextBox(T.FontSize + 1, readOnly: false);
        _sourceBox.AcceptsReturn = true;
        _sourceBox.MinHeight = 26;
        _sourceBox.MaxHeight = 160;
        _placeholder = PopupUi.Text("Type or paste text, then press Enter", T.FontSize + 1, "Faint");
        _placeholder.IsHitTestVisible = false;
        var sourceField = new Grid { Margin = new Thickness(4, 0, 4, 0), Background = Brushes.Transparent, Cursor = Cursors.IBeam };
        sourceField.Children.Add(_placeholder);
        sourceField.Children.Add(_sourceBox);
        sourceField.MouseLeftButtonDown += (_, _) => _sourceBox.Focus();

        _sourceBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                _typingTimer!.Stop();
                _ = TranslateAsync();
            }
        };
        _sourceBox.TextChanged += (_, _) =>
        {
            _placeholder.Visibility = _sourceBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_quiet) return;
            UpdateSourceDirection();
            _typingTimer!.Stop();
            _typingTimer.Start();
        };

        _detected = PopupUi.Text("", 12, "Muted");
        _detected.VerticalAlignment = VerticalAlignment.Center;
        _detected.TextWrapping = TextWrapping.NoWrap;
        _detected.TextTrimming = TextTrimming.CharacterEllipsis;
        _detected.Margin = new Thickness(6, 0, 8, 0);
        _modeLabel = PopupUi.Text("", 12, "Muted");
        _modeLabel.Margin = new Thickness(4, 0, 0, 8);
        _modeLabel.Visibility = Visibility.Collapsed;

        var sourceTools = new DockPanel { Margin = new Thickness(-2, 10, 0, 0) };
        var sourceLeft = new StackPanel { Orientation = Orientation.Horizontal };
        sourceLeft.Children.Add(PopupUi.IconButton(SymbolRegular.Speaker220, "Listen", () => Speak(_sourceBox.Text, SourceLanguageForSpeech())));
        sourceLeft.Children.Add(PopupUi.IconButton(SymbolRegular.Copy20, "Copy text", () => Copy(_sourceBox.Text)));
        sourceLeft.Children.Add(_detected);
        var translateButton = PopupUi.PillButton("Translate", () => _ = TranslateAsync());
        DockPanel.SetDock(translateButton, Dock.Right);
        sourceTools.Children.Add(translateButton);
        sourceTools.Children.Add(sourceLeft);

        var top = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
        top.Children.Add(header);
        top.Children.Add(_modeLabel);
        top.Children.Add(sourceField);
        top.Children.Add(sourceTools);

        // ---- Result: the tinted lower half ---------------------------------------------
        _resultBox = PopupUi.PlainTextBox(T.FontSize + 3, readOnly: true);
        _resultBox.FontWeight = FontWeights.Medium;
        _resultBox.MaxHeight = 320;
        _translit = PopupUi.Text("", 12, "Muted");
        _translit.FontStyle = FontStyles.Italic;
        _translit.Margin = new Thickness(0, 4, 0, 0);
        var resultTools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-8, 6, 0, 0) };
        resultTools.Children.Add(PopupUi.IconButton(SymbolRegular.Speaker220, "Listen", () => Speak(_resultBox.Text, _last?.TargetLanguage ?? _targetPicker.Code)));
        resultTools.Children.Add(PopupUi.IconButton(SymbolRegular.Copy20, "Copy translation", () => Copy(_resultBox.Text)));
        var resultStack = new StackPanel();
        resultStack.Children.Add(_resultBox);
        resultStack.Children.Add(_translit);
        resultStack.Children.Add(resultTools);
        _resultCard = new Border { Child = resultStack };

        // ---- Second language -----------------------------------------------------------
        _secondLabel = PopupUi.Text("", 11, "Muted", FontWeights.SemiBold);
        _secondLabel.Margin = new Thickness(0, 10, 0, 4);
        _secondBox = PopupUi.PlainTextBox(T.FontSize + 1, readOnly: true);
        _secondBox.MaxHeight = 200;
        var secondStack = new StackPanel();
        secondStack.Children.Add(_secondLabel);
        secondStack.Children.Add(_secondBox);
        _secondCard = new Border { Child = secondStack, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 8, 0, 0) };
        _secondCard.SetResourceReference(Border.BorderBrushProperty, "Adawat.TintBorder");
        _secondCard.Visibility = Visibility.Collapsed;

        _dictionaryPanel.Margin = new Thickness(0, 10, 0, 0);

        // ---- Footer ---------------------------------------------------------------------
        _status = PopupUi.Text("", 12, "Muted");
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        _status.TextWrapping = TextWrapping.NoWrap;
        _googleLink = PopupUi.Link("Open in Google Translate", OpenInGoogle);
        _footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(_googleLink, Dock.Right);
        _footer.Children.Add(_googleLink);
        _footer.Children.Add(_status);

        var bottomStack = new StackPanel();
        bottomStack.Children.Add(_resultCard);
        bottomStack.Children.Add(_secondCard);
        bottomStack.Children.Add(_dictionaryPanel);
        bottomStack.Children.Add(_footer);
        _bottom = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(18, 14, 18, 12), Child = bottomStack };
        _bottom.SetResourceReference(Border.BackgroundProperty, "Adawat.Tint");
        _bottom.SetResourceReference(Border.BorderBrushProperty, "Adawat.TintBorder");

        var root = new StackPanel();
        root.Children.Add(top);
        root.Children.Add(_bottom);
        // The scroller measures the content at its natural height; the window then takes that height.
        var scroller = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };
        root.SizeChanged += (_, _) => Height = Math.Min(MaxHeight, Math.Ceiling(root.ActualHeight));
        Content = scroller;

        _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _typingTimer.Tick += (_, _) =>
        {
            _typingTimer.Stop();
            _ = TranslateAsync();
        };

        _sourcePicker.Changed += OnLanguageChanged;
        _targetPicker.Changed += OnLanguageChanged;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (_sourcePicker.IsOpen || _targetPicker.IsOpen)
            {
                _sourcePicker.Close();
                _targetPicker.Close();
            }
            else
            {
                HideWindow();
            }
        };
        Deactivated += (_, _) =>
        {
            if (!_pinned && T.CloseOnClickOutside && IsVisible) HideWindow();
        };
        Closing += (_, e) =>
        {
            // Keep the window for next time; it opens much faster than a new one.
            e.Cancel = true;
            HideWindow();
        };
        SizeChanged += (_, _) => KeepOnScreen();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyBorderColor();
        };
        ThemeHelper.Changed += () =>
        {
            ApplyTheme();
            ApplyBorderColor();
            if (_statusIsError) SetStatus(_status.Text, error: true);
        };
    }

    private void ApplyTheme() => ThemeHelper.ApplyPalette(Resources);

    /// <summary>Windows 11 draws a thin border around the window; give it the palette's border color.</summary>
    private void ApplyBorderColor()
    {
        if (_hwnd == IntPtr.Zero) return;
        var c = ThemeHelper.PaletteColor("Border");
        int colorRef = c.R | (c.G << 8) | (c.B << 16);
        Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));
    }

    // ---- Opening -----------------------------------------------------------------------

    /// <summary>Shows the popup near <paramref name="anchor"/> (physical screen pixels).</summary>
    public void ShowFor(string? text, PopupMode mode, Native.POINT anchor, bool translateNow = true, string? error = null)
    {
        CommitHistory();
        _cts?.Cancel();
        _typingTimer.Stop();
        _speech.Stop();
        _mode = mode;
        _anchor = anchor;

        Width = Math.Clamp(T.PopupWidth, 440, 900);
        _sourceBox.FontSize = T.FontSize + 1;
        _placeholder.FontSize = T.FontSize + 1;
        _resultBox.FontSize = T.FontSize + 3;
        _secondBox.FontSize = T.FontSize + 1;
        _googleLink.Visibility = T.ShowOpenInGoogle ? Visibility.Visible : Visibility.Collapsed;
        FillLanguagePickers();

        _quiet = true;
        _sourceBox.Text = text ?? "";
        _quiet = false;
        UpdateSourceDirection();
        ClearResult();
        _modeLabel.Text = mode == PopupMode.Image ? "Text found on screen. You can edit it before translating." : "";
        _modeLabel.Visibility = mode == PopupMode.Image ? Visibility.Visible : Visibility.Collapsed;
        if (error != null)
            SetStatus(error, error: true);
        else if (mode == PopupMode.Image && string.IsNullOrWhiteSpace(text))
            SetStatus("No text was found in that picture.");

        bool firstShow = !IsVisible;
        if (firstShow)
        {
            Left = -10000;
            Top = -10000;
            Show();
        }
        UpdateLayout();
        PlaceNearAnchor();
        Activate();
        Native.SetForegroundWindow(_hwnd);

        if (string.IsNullOrWhiteSpace(text))
        {
            _sourceBox.Focus();
            Keyboard.Focus(_sourceBox);
        }
        else
        {
            Keyboard.Focus(_resultBox);
            if (translateNow) _ = TranslateAsync();
        }
    }

    public void ShowHistoryEntry(HistoryEntry entry)
    {
        ShowFor(entry.Source, PopupMode.Manual, WindowInfo.CursorPosition(), translateNow: false);
        _resultBox.Text = entry.Translation;
        _resultBox.FlowDirection = Languages.IsRightToLeft(entry.TargetLanguage) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _detected.Text = $"{Languages.NameOf(entry.SourceLanguage)} → {Languages.NameOf(entry.TargetLanguage)}";
        _resultCard.Visibility = Visibility.Visible;
        UpdateBottom();
    }

    private void PlaceNearAnchor()
    {
        if (_hwnd == IntPtr.Zero || !Native.GetWindowRect(_hwnd, out var r)) return;
        var work = WindowInfo.WorkAreaAt(_anchor);
        double scale = WindowInfo.ScaleAt(_anchor);
        int gap = (int)(18 * scale);
        int x = _anchor.X - r.Width / 2;
        int y = _anchor.Y + gap;
        if (y + r.Height > work.Bottom) y = Math.Max(work.Top, _anchor.Y - gap - r.Height);
        x = Math.Clamp(x, work.Left + 8, Math.Max(work.Left + 8, work.Right - r.Width - 8));
        y = Math.Clamp(y, work.Top + 8, Math.Max(work.Top + 8, work.Bottom - r.Height - 8));
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>When the window grows (translation arrived), move it up if it would leave the screen.</summary>
    private void KeepOnScreen()
    {
        if (!IsVisible || _hwnd == IntPtr.Zero || !Native.GetWindowRect(_hwnd, out var r)) return;
        var work = WindowInfo.WorkAreaAt(new Native.POINT(r.Left + r.Width / 2, r.Top + 10));
        int y = r.Top;
        if (r.Bottom > work.Bottom - 8) y = Math.Max(work.Top + 8, work.Bottom - 8 - r.Height);
        if (y != r.Top) Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, r.Left, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public void HideWindow()
    {
        _typingTimer.Stop();
        _cts?.Cancel();
        _speech.Stop();
        CommitHistory();
        if (_pinned) TogglePin();
        Hide();
    }

    private void TogglePin()
    {
        _pinned = !_pinned;
        PopupUi.SetActive(_pinButton, _pinned);
        _pinButton.ToolTip = _pinned ? "Stop keeping open" : "Keep open";
    }

    // ---- Languages ---------------------------------------------------------------------

    private void FillLanguagePickers()
    {
        var ordered = Languages.Ordered(T.FavoriteLanguages).Select(l => (l.Code, l.Name)).ToList();
        _sourcePicker.SetLanguages(new[] { (Languages.Auto, "Detect language") }.Concat(ordered));
        _targetPicker.SetLanguages(ordered);
        _sourcePicker.Select(T.SourceLanguage);
        _targetPicker.Select(T.TargetLanguage);
    }

    private void OnLanguageChanged()
    {
        T.SourceLanguage = _sourcePicker.Code;
        T.TargetLanguage = _targetPicker.Code;
        _saveSettings();
        if (!string.IsNullOrWhiteSpace(_sourceBox.Text)) _ = TranslateAsync();
    }

    private void Swap()
    {
        var source = _sourcePicker.Code;
        var target = _targetPicker.Code;
        var newTarget = source == Languages.Auto ? (_last?.SourceLanguage is { Length: > 0 } s ? s : "en") : source;

        _quiet = true;
        if (!string.IsNullOrWhiteSpace(_resultBox.Text)) _sourceBox.Text = _resultBox.Text;
        _sourcePicker.Select(target);
        _targetPicker.Select(newTarget);
        _quiet = false;
        UpdateSourceDirection();

        T.SourceLanguage = _sourcePicker.Code;
        T.TargetLanguage = _targetPicker.Code;
        _saveSettings();
        _ = TranslateAsync();
    }

    private string SourceLanguageForSpeech()
    {
        var code = _sourcePicker.Code;
        return code == Languages.Auto ? (_last?.SourceLanguage is { Length: > 0 } s ? s : "en") : code;
    }

    // ---- Translating -------------------------------------------------------------------

    public async Task TranslateAsync()
    {
        var text = _sourceBox.Text.Trim();
        _cts?.Cancel();
        if (text.Length == 0)
        {
            ClearResult();
            return;
        }

        var cts = _cts = new CancellationTokenSource();
        var source = _sourcePicker.Code;
        var target = _targetPicker.Code;
        SetStatus("Translating…");

        try
        {
            var result = await _translator.TranslateAsync(text, source, target, cts.Token);
            bool usedFallback = false;
            if (source == Languages.Auto && Languages.Same(result.SourceLanguage, target)
                && !string.IsNullOrEmpty(T.FallbackTargetLanguage) && !Languages.Same(T.FallbackTargetLanguage, target))
            {
                // The text is already in the target language (e.g. Arabic text with Arabic as target).
                result = await _translator.TranslateAsync(text, source, T.FallbackTargetLanguage, cts.Token);
                usedFallback = true;
            }
            if (cts.IsCancellationRequested) return;

            ShowResult(result, usedFallback, target);
            SetStatus("");

            if (T.AutoCopyTranslation) Copy(result.Translation, quiet: true);
            _pendingHistory = result;
            if (_mode != PopupMode.Manual) CommitHistory();

            var second = T.SecondTargetLanguage;
            if (!string.IsNullOrEmpty(second) && !Languages.Same(second, result.TargetLanguage) && !Languages.Same(second, result.SourceLanguage))
            {
                var extra = await _translator.TranslateAsync(text, source, second, cts.Token);
                if (cts.IsCancellationRequested) return;
                _secondLabel.Text = Languages.NameOf(second).ToUpperInvariant();
                _secondBox.Text = extra.Translation;
                _secondBox.FlowDirection = Languages.IsRightToLeft(second) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                _secondCard.Visibility = Visibility.Visible;
            }
            else
            {
                _secondCard.Visibility = Visibility.Collapsed;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (TranslationException ex)
        {
            if (!cts.IsCancellationRequested) SetStatus(ex.Message, error: true);
        }
        catch (Exception ex)
        {
            Log.Error("Translation failed", ex);
            if (!cts.IsCancellationRequested) SetStatus("Something went wrong while translating.", error: true);
        }
    }

    private void ShowResult(TranslationResult result, bool usedFallback, string requestedTarget)
    {
        _last = result;
        _resultCard.Visibility = Visibility.Visible;
        _resultBox.Text = result.Translation;
        _resultBox.FlowDirection = Languages.IsRightToLeft(result.TargetLanguage) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        if (usedFallback)
            _detected.Text = $"Already {Languages.NameOf(requestedTarget)}: showing {Languages.NameOf(result.TargetLanguage)}";
        else if (_sourcePicker.Code == Languages.Auto && !string.IsNullOrEmpty(result.SourceLanguage))
            _detected.Text = $"Detected: {Languages.NameOf(result.SourceLanguage)}";
        else
            _detected.Text = "";

        var translit = T.ShowTransliteration ? result.Transliteration : null;
        _translit.Text = translit ?? "";
        _translit.Visibility = string.IsNullOrWhiteSpace(translit) || translit == result.Translation ? Visibility.Collapsed : Visibility.Visible;

        _dictionaryPanel.Children.Clear();
        if (T.ShowDictionary)
        {
            foreach (var entry in result.Dictionary.Take(5))
            {
                var line = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) };
                line.SetResourceReference(TextBlock.ForegroundProperty, "Adawat.Text");
                if (!string.IsNullOrEmpty(entry.PartOfSpeech))
                {
                    var pos = new Run(entry.PartOfSpeech + "   ") { FontWeight = FontWeights.SemiBold, FontSize = 12 };
                    pos.SetResourceReference(TextElement.ForegroundProperty, "Adawat.Accent");
                    line.Inlines.Add(pos);
                }
                line.Inlines.Add(new Run(string.Join(", ", entry.Terms.Take(8))));
                line.FlowDirection = Languages.IsRightToLeft(result.TargetLanguage) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                _dictionaryPanel.Children.Add(line);
            }
        }
        _dictionaryPanel.Visibility = _dictionaryPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearResult()
    {
        _last = null;
        _resultBox.Text = "";
        _translit.Text = "";
        _translit.Visibility = Visibility.Collapsed;
        _detected.Text = "";
        _dictionaryPanel.Children.Clear();
        _dictionaryPanel.Visibility = Visibility.Collapsed;
        _secondCard.Visibility = Visibility.Collapsed;
        _resultCard.Visibility = Visibility.Collapsed;
        SetStatus("");
    }

    private void UpdateSourceDirection()
    {
        _sourceBox.FlowDirection = Languages.LooksRightToLeft(_sourceBox.Text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    private void CommitHistory()
    {
        if (_pendingHistory == null) return;
        try
        {
            _history.Add(_pendingHistory);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save to history", ex);
        }
        _pendingHistory = null;
    }

    private void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ToolTip = string.IsNullOrEmpty(text) ? null : text;
        _statusIsError = error;
        if (error) _status.Foreground = new SolidColorBrush(ThemeHelper.IsDark ? Color.FromRgb(0xFF, 0x99, 0xA4) : Color.FromRgb(0xC4, 0x2B, 0x1C));
        else _status.SetResourceReference(TextBlock.ForegroundProperty, "Adawat.Muted");
        _footer.Visibility = text.Length > 0 || T.ShowOpenInGoogle ? Visibility.Visible : Visibility.Collapsed;
        UpdateBottom();
    }

    /// <summary>The tinted lower half only shows when it has something: a translation, a message or the link.</summary>
    private void UpdateBottom()
    {
        bool result = _resultCard.Visibility == Visibility.Visible;
        _bottom.Visibility = result || _footer.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        _footer.Margin = new Thickness(0, result ? 10 : 0, 0, 0);
    }

    // ---- Actions -----------------------------------------------------------------------

    private void Speak(string text, string language)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _ = _speech.SpeakAsync(text, language);
    }

    private void Copy(string text, bool quiet = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                if (!quiet) SetStatus("Copied");
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(30);
            }
        }
    }

    private void OpenInGoogle()
    {
        var url = "https://translate.google.com/?sl=" + Uri.EscapeDataString(_sourcePicker.Code)
                  + "&tl=" + Uri.EscapeDataString(_targetPicker.Code)
                  + "&text=" + Uri.EscapeDataString(_sourceBox.Text.Length > 4000 ? _sourceBox.Text[..4000] : _sourceBox.Text)
                  + "&op=translate";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Could not open the browser", ex);
        }
        HideWindow();
    }
}
