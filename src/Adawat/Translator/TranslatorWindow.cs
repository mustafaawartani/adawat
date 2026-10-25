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

    private readonly ComboBox _sourceCombo = new() { Width = 136, MaxDropDownHeight = 420 };
    private readonly ComboBox _targetCombo = new() { Width = 136, MaxDropDownHeight = 420 };
    private readonly Wpf.Ui.Controls.TextBox _sourceBox;
    private readonly TextBox _resultBox;
    private readonly TextBox _secondBox;
    private readonly TextBlock _detected;
    private readonly TextBlock _translit;
    private readonly TextBlock _secondLabel;
    private readonly TextBlock _status;
    private readonly TextBlock _modeLabel;
    private readonly StackPanel _dictionaryPanel = new();
    private readonly Border _resultCard;
    private readonly Border _secondCard;
    private readonly Wpf.Ui.Controls.Button _pinButton;
    private readonly DispatcherTimer _typingTimer;

    private CancellationTokenSource? _cts;
    private TranslationResult? _last;
    private TranslationResult? _pendingHistory;
    private PopupMode _mode;
    private Native.POINT _anchor;
    private bool _pinned;
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
        WindowBackdropType = WindowBackdropType.Acrylic;
        WindowCornerPreference = WindowCornerPreference.Round;
        ExtendsContentIntoTitleBar = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000;
        Top = -10000;
        MaxHeight = 760;
        FontFamily = new FontFamily(Ui.FontFamilyName);

        // ---- Header: languages and window buttons ---------------------------------
        var swap = Ui.IconButton(SymbolRegular.ArrowSwap24, "Swap languages", Swap);
        _pinButton = Ui.IconButton(SymbolRegular.Pin24, "Keep open", TogglePin);
        var header = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 10) };
        var languages = new StackPanel { Orientation = Orientation.Horizontal };
        languages.Children.Add(_sourceCombo);
        languages.Children.Add(swap);
        languages.Children.Add(_targetCombo);
        DockPanel.SetDock(languages, Dock.Left);
        header.Children.Add(languages);
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(_pinButton);
        tools.Children.Add(Ui.IconButton(SymbolRegular.Settings24, "Settings", () => { HideWindow(); _openSettings(); }));
        tools.Children.Add(Ui.IconButton(SymbolRegular.Dismiss24, "Close (Esc)", HideWindow));
        DockPanel.SetDock(tools, Dock.Right);
        header.Children.Add(tools);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is Grid or DockPanel or StackPanel) DragMove();
        };
        foreach (UIElement child in new UIElement[] { languages, tools }) WindowChrome.SetIsHitTestVisibleInChrome(child, true);

        // ---- Source text -------------------------------------------------------------
        _sourceBox = new Wpf.Ui.Controls.TextBox
        {
            PlaceholderText = "Type or paste text, then press Enter",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 44,
            MaxHeight = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ClearButtonEnabled = false,
        };
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
            if (_quiet) return;
            UpdateSourceDirection();
            _typingTimer!.Stop();
            _typingTimer.Start();
        };

        _detected = Ui.Text("", 12, secondary: true);
        _detected.VerticalAlignment = VerticalAlignment.Center;
        _detected.Margin = new Thickness(4, 0, 0, 0);
        _modeLabel = Ui.Text("", 12, secondary: true);
        _modeLabel.Margin = new Thickness(2, 0, 0, 6);
        _modeLabel.Visibility = Visibility.Collapsed;

        var sourceTools = new DockPanel { Margin = new Thickness(0, 4, 0, 8) };
        var sourceLeft = new StackPanel { Orientation = Orientation.Horizontal };
        sourceLeft.Children.Add(Ui.IconButton(SymbolRegular.Speaker224, "Listen", () => Speak(_sourceBox.Text, SourceLanguageForSpeech())));
        sourceLeft.Children.Add(Ui.IconButton(SymbolRegular.Copy24, "Copy text", () => Copy(_sourceBox.Text)));
        sourceLeft.Children.Add(_detected);
        var translateButton = Ui.Button("Translate", () => _ = TranslateAsync(), primary: true);
        translateButton.Padding = new Thickness(12, 4, 12, 5);
        DockPanel.SetDock(translateButton, Dock.Right);
        sourceTools.Children.Add(translateButton);
        sourceTools.Children.Add(sourceLeft);

        // ---- Result -------------------------------------------------------------------
        _resultBox = Ui.SelectableText(T.FontSize + 2);
        _resultBox.MaxHeight = 320;
        _translit = Ui.Text("", 12, secondary: true);
        _translit.Margin = new Thickness(0, 4, 0, 0);
        var resultTools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 6, 0, -4) };
        resultTools.Children.Add(Ui.IconButton(SymbolRegular.Speaker224, "Listen", () => Speak(_resultBox.Text, _last?.TargetLanguage ?? SelectedCode(_targetCombo))));
        resultTools.Children.Add(Ui.IconButton(SymbolRegular.Copy24, "Copy translation", () => Copy(_resultBox.Text)));
        var resultStack = new StackPanel();
        resultStack.Children.Add(_resultBox);
        resultStack.Children.Add(_translit);
        resultStack.Children.Add(resultTools);
        _resultCard = Card(resultStack);

        // ---- Second language -----------------------------------------------------------
        _secondLabel = Ui.Text("", 12, secondary: true);
        _secondBox = Ui.SelectableText(T.FontSize);
        _secondBox.MaxHeight = 200;
        var secondStack = new StackPanel();
        secondStack.Children.Add(_secondLabel);
        secondStack.Children.Add(_secondBox);
        _secondCard = Card(secondStack);
        _secondCard.Margin = new Thickness(0, 8, 0, 0);
        _secondCard.Visibility = Visibility.Collapsed;

        _dictionaryPanel.Margin = new Thickness(2, 10, 2, 0);

        // ---- Footer ---------------------------------------------------------------------
        _status = Ui.Text("", 12, secondary: true);
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        _status.TextWrapping = TextWrapping.NoWrap;
        var link = new Hyperlink(new Run("Open in Google Translate"));
        link.Click += (_, _) => OpenInGoogle();
        link.SetResourceReference(TextElement.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        var linkBlock = new TextBlock(link) { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var footer = new DockPanel { Margin = new Thickness(2, 10, 2, 0) };
        DockPanel.SetDock(linkBlock, Dock.Right);
        footer.Children.Add(linkBlock);
        footer.Children.Add(_status);

        var root = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
        root.Children.Add(header);
        root.Children.Add(_modeLabel);
        root.Children.Add(_sourceBox);
        root.Children.Add(sourceTools);
        root.Children.Add(_resultCard);
        root.Children.Add(_secondCard);
        root.Children.Add(_dictionaryPanel);
        root.Children.Add(footer);
        Content = root;

        _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _typingTimer.Tick += (_, _) =>
        {
            _typingTimer.Stop();
            _ = TranslateAsync();
        };

        _sourceCombo.SelectionChanged += (_, _) => OnLanguageChanged();
        _targetCombo.SelectionChanged += (_, _) => OnLanguageChanged();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
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
        SourceInitialized += (_, _) => _hwnd = new WindowInteropHelper(this).Handle;
    }

    private static Border Card(UIElement child)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 8),
            BorderThickness = new Thickness(1),
            Child = child,
        };
        border.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        return border;
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
        _sourceBox.FontSize = T.FontSize;
        _resultBox.FontSize = T.FontSize + 2;
        _secondBox.FontSize = T.FontSize;
        FillLanguageCombos();

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
        _pinButton.Icon = Ui.Icon(_pinned ? SymbolRegular.PinOff24 : SymbolRegular.Pin24);
        _pinButton.ToolTip = _pinned ? "Stop keeping open" : "Keep open";
    }

    // ---- Languages ---------------------------------------------------------------------

    private void FillLanguageCombos()
    {
        _quiet = true;
        try
        {
            var ordered = Languages.Ordered(T.FavoriteLanguages);
            _sourceCombo.Items.Clear();
            _sourceCombo.Items.Add(new ComboBoxItem { Content = "Detect language", Tag = Languages.Auto });
            foreach (var l in ordered) _sourceCombo.Items.Add(new ComboBoxItem { Content = l.Name, Tag = l.Code });
            _targetCombo.Items.Clear();
            foreach (var l in ordered) _targetCombo.Items.Add(new ComboBoxItem { Content = l.Name, Tag = l.Code });
            Select(_sourceCombo, T.SourceLanguage);
            Select(_targetCombo, T.TargetLanguage);
        }
        finally
        {
            _quiet = false;
        }
    }

    private static void Select(ComboBox combo, string code)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (Languages.Same(item.Tag as string, code))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static string SelectedCode(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? Languages.Auto;

    private void OnLanguageChanged()
    {
        if (_quiet) return;
        T.SourceLanguage = SelectedCode(_sourceCombo);
        T.TargetLanguage = SelectedCode(_targetCombo);
        _saveSettings();
        if (!string.IsNullOrWhiteSpace(_sourceBox.Text)) _ = TranslateAsync();
    }

    private void Swap()
    {
        var source = SelectedCode(_sourceCombo);
        var target = SelectedCode(_targetCombo);
        var newTarget = source == Languages.Auto ? (_last?.SourceLanguage is { Length: > 0 } s ? s : "en") : source;

        _quiet = true;
        if (!string.IsNullOrWhiteSpace(_resultBox.Text)) _sourceBox.Text = _resultBox.Text;
        Select(_sourceCombo, target);
        Select(_targetCombo, newTarget);
        _quiet = false;
        UpdateSourceDirection();

        T.SourceLanguage = SelectedCode(_sourceCombo);
        T.TargetLanguage = SelectedCode(_targetCombo);
        _saveSettings();
        _ = TranslateAsync();
    }

    private string SourceLanguageForSpeech()
    {
        var code = SelectedCode(_sourceCombo);
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
        var source = SelectedCode(_sourceCombo);
        var target = SelectedCode(_targetCombo);
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
                _secondLabel.Text = Languages.NameOf(second);
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
        else if (SelectedCode(_sourceCombo) == Languages.Auto && !string.IsNullOrEmpty(result.SourceLanguage))
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
                var line = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) };
                line.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                if (!string.IsNullOrEmpty(entry.PartOfSpeech))
                {
                    var pos = new Run(entry.PartOfSpeech + "  ") { FontWeight = FontWeights.SemiBold };
                    pos.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
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
        if (error) _status.Foreground = new SolidColorBrush(ThemeHelper.IsDark ? Color.FromRgb(0xFF, 0x99, 0xA4) : Color.FromRgb(0xC4, 0x2B, 0x1C));
        else _status.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
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
        var url = "https://translate.google.com/?sl=" + Uri.EscapeDataString(SelectedCode(_sourceCombo))
                  + "&tl=" + Uri.EscapeDataString(SelectedCode(_targetCombo))
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
