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
    private readonly FrameworkElement _translateButton;
    private readonly Grid _divider;
    private readonly Border _progressBar;
    private readonly StackPanel _resultSection;
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
    private readonly StackPanel _resultCard;
    private readonly Border _resultFrame;
    private readonly StackPanel _secondCard;
    private readonly Border _pinButton;
    private readonly DispatcherTimer _typingTimer;

    private CancellationTokenSource? _cts;
    private TranslationResult? _last;
    private TranslationResult? _pendingHistory;
    private PopupMode _mode;
    private Native.POINT _anchor;
    private bool _pinned;
    private bool _statusIsError;
    private bool _busy;
    private bool _placedAbove;   // the popup sits above the selection: it grows upward
    private string _translatedText = "";
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
        // One solid surface: no see-through glass (it turned grey whenever the window was not
        // in front, and showed through as an empty grey area under the content).
        WindowBackdropType = WindowBackdropType.None;
        WindowCornerPreference = WindowCornerPreference.Round;
        ExtendsContentIntoTitleBar = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000;
        Top = -10000;
        // WPF UI's window style asks for at least 320 x 460 DIPs, which left an empty strip under
        // short translations. The window takes exactly the height of its content instead.
        MinHeight = 0;
        MinWidth = 0;
        MaxHeight = 760;
        FontFamily = new FontFamily(Ui.FontFamilyName);
        ApplyTheme();
        SetResourceReference(BackgroundProperty, "Adawat.Wash");

        // ---- Header: from / swap / to on the left, window buttons on the right -----------
        var swap = PopupUi.IconButton(SymbolRegular.ArrowSwap20, "Swap languages", Swap, 15, brush: "AccentText");
        swap.Margin = new Thickness(4, 0, 4, 0);
        _targetPicker.Strong = true;
        _pinButton = PopupUi.IconButton(SymbolRegular.Pin20, "Keep open", TogglePin);
        var languages = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        languages.Children.Add(_sourcePicker);
        languages.Children.Add(swap);
        languages.Children.Add(_targetPicker);
        var header = new DockPanel { LastChildFill = false, Margin = new Thickness(10, 8, 8, 0), Background = Brushes.Transparent };
        DockPanel.SetDock(languages, Dock.Left);
        header.Children.Add(languages);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(_pinButton);
        tools.Children.Add(PopupUi.IconButton(SymbolRegular.Settings20, "Settings", () => { HideWindow(); _openSettings(); }));
        tools.Children.Add(PopupUi.IconButton(SymbolRegular.Dismiss20, "Close (Esc)", HideWindow));
        DockPanel.SetDock(tools, Dock.Right);
        header.Children.Add(tools);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not DockPanel) return;
            _placedAbove = false; // moved by hand: stay where it was put
            DragMove();
        };
        foreach (UIElement child in new UIElement[] { languages, tools }) WindowChrome.SetIsHitTestVisibleInChrome(child, true);

        // ---- Source text: no box, just the text ---------------------------------------
        _sourceBox = PopupUi.PlainTextBox(T.FontSize, readOnly: false);
        _sourceBox.AcceptsReturn = true;
        _sourceBox.MinHeight = 24;
        _sourceBox.MaxHeight = 160;
        _placeholder = PopupUi.Text("Type or paste text", T.FontSize, "Faint");
        _placeholder.IsHitTestVisible = false;
        var sourceField = new Grid { Background = Brushes.Transparent, Cursor = Cursors.IBeam };
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
            UpdateTranslateButton();
            if (_quiet) return;
            UpdateSourceDirection();
            _typingTimer!.Stop();
            _typingTimer.Start();
        };

        _detected = PopupUi.Text("", 12, "AccentText");
        _detected.VerticalAlignment = VerticalAlignment.Center;
        _detected.TextWrapping = TextWrapping.NoWrap;
        _detected.TextTrimming = TextTrimming.CharacterEllipsis;
        _detected.Margin = new Thickness(6, 0, 8, 0);
        _modeLabel = PopupUi.Text("", 12, "Muted");
        _modeLabel.Margin = new Thickness(0, 0, 0, 6);
        _modeLabel.Visibility = Visibility.Collapsed;

        var sourceTools = new DockPanel { Margin = new Thickness(10, 4, 14, 8), LastChildFill = true };
        var sourceLeft = new StackPanel { Orientation = Orientation.Horizontal };
        sourceLeft.Children.Add(PopupUi.IconButton(SymbolRegular.Speaker220, "Listen", () => Speak(_sourceBox.Text, SourceLanguageForSpeech())));
        sourceLeft.Children.Add(PopupUi.IconButton(SymbolRegular.Copy20, "Copy text", () => Copy(_sourceBox.Text)));
        sourceLeft.Children.Add(_detected);
        _translateButton = PopupUi.PillButton("Translate", () => _ = TranslateAsync());
        _translateButton.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(_translateButton, Dock.Right);
        sourceTools.Children.Add(_translateButton);
        sourceTools.Children.Add(sourceLeft);

        var source = new StackPanel { Margin = new Thickness(18, 12, 18, 0) };
        source.Children.Add(_modeLabel);
        source.Children.Add(sourceField);

        // ---- A hairline between text and translation; a thin bar runs along it while translating
        _progressBar = new Border { Width = 90, Height = 2, CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Left };
        _progressBar.SetResourceReference(Border.BackgroundProperty, "Adawat.Accent");
        _progressBar.RenderTransform = new TranslateTransform();
        _progressBar.Visibility = Visibility.Collapsed;
        var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center };
        line.SetResourceReference(Border.BackgroundProperty, "Adawat.AccentSoft");
        _divider = new Grid { Height = 2, Margin = new Thickness(18, 0, 18, 0), ClipToBounds = true };
        _divider.Children.Add(line);
        _divider.Children.Add(_progressBar);

        // ---- Result --------------------------------------------------------------------
        _resultBox = PopupUi.PlainTextBox(T.FontSize + 2, readOnly: true);
        _resultBox.MaxHeight = 320;
        _translit = PopupUi.Text("", 12, "Muted");
        _translit.Margin = new Thickness(0, 4, 0, 0);
        _resultCard = new StackPanel();
        _resultCard.Children.Add(_resultBox);
        _resultCard.Children.Add(_translit);
        // A slim accent bar on the side the translation starts from.
        _resultFrame = new Border { BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 2, 0, 2), Child = _resultCard };
        _resultFrame.SetResourceReference(Border.BorderBrushProperty, "Adawat.Accent");

        _secondLabel = PopupUi.Text("", 11, "Muted", FontWeights.SemiBold);
        _secondLabel.Margin = new Thickness(0, 14, 0, 3);
        _secondBox = PopupUi.PlainTextBox(T.FontSize, readOnly: true);
        _secondBox.MaxHeight = 200;
        _secondCard = new StackPanel { Visibility = Visibility.Collapsed };
        _secondCard.Children.Add(_secondLabel);
        _secondCard.Children.Add(_secondBox);

        _dictionaryPanel.Margin = new Thickness(0, 12, 0, 0);

        var resultTools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-8, 6, 0, 0) };
        resultTools.Children.Add(PopupUi.IconButton(SymbolRegular.Speaker220, "Listen", () => Speak(_resultBox.Text, _last?.TargetLanguage ?? _targetPicker.Code), brush: "AccentText"));
        resultTools.Children.Add(PopupUi.IconButton(SymbolRegular.Copy20, "Copy translation", () => Copy(_resultBox.Text), brush: "AccentText"));

        _resultSection = new StackPanel { Margin = new Thickness(18, 14, 18, 0), Visibility = Visibility.Collapsed };
        _resultSection.Children.Add(_resultFrame);
        _resultSection.Children.Add(_secondCard);
        _resultSection.Children.Add(_dictionaryPanel);
        _resultSection.Children.Add(resultTools);

        // ---- Footer ---------------------------------------------------------------------
        _status = PopupUi.Text("", 12, "Muted");
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        _status.TextWrapping = TextWrapping.NoWrap;
        _googleLink = PopupUi.Link("Google Translate ↗", OpenInGoogle);
        _googleLink.ToolTip = "Open in Google Translate";
        _footer = new DockPanel { Margin = new Thickness(18, 2, 18, 12) };
        DockPanel.SetDock(_googleLink, Dock.Right);
        _footer.Children.Add(_googleLink);
        _footer.Children.Add(_status);

        var root = new StackPanel();
        root.Children.Add(header);
        root.Children.Add(source);
        root.Children.Add(sourceTools);
        root.Children.Add(_divider);
        root.Children.Add(_resultSection);
        root.Children.Add(_footer);
        // The scroller lets the content take its natural height; the window then takes exactly that.
        var scroller = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };
        root.SizeChanged += (_, _) => FitHeight(root);
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
        DpiChanged += (_, _) => FitHeight(root);
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
        var c = ThemeHelper.PaletteColor("AccentLine");
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

        Width = Math.Clamp(T.PopupWidth, 400, 900);
        MaxHeight = Math.Max(260, WindowInfo.WorkAreaAt(anchor).Height / WindowInfo.ScaleAt(anchor) - 24);
        _sourceBox.FontSize = T.FontSize;
        _placeholder.FontSize = T.FontSize;
        _resultBox.FontSize = T.FontSize + 2;
        _secondBox.FontSize = T.FontSize;
        _googleLink.Visibility = T.ShowOpenInGoogle ? Visibility.Visible : Visibility.Collapsed;
        FillLanguagePickers();

        _quiet = true;
        _sourceBox.Text = text ?? "";
        _quiet = false;
        _translatedText = "";
        UpdateTranslateButton();
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
        _resultFrame.FlowDirection = _resultBox.FlowDirection;
        _detected.Text = $"{Languages.NameOf(entry.SourceLanguage)} → {Languages.NameOf(entry.TargetLanguage)}";
        _translatedText = _sourceBox.Text.Trim();
        _resultSection.Visibility = Visibility.Visible;
        UpdateSections();
    }

    private void PlaceNearAnchor()
    {
        if (_hwnd == IntPtr.Zero || !Native.GetWindowRect(_hwnd, out var r)) return;
        var work = WindowInfo.WorkAreaAt(_anchor);
        double scale = WindowInfo.ScaleAt(_anchor);
        int gap = (int)(18 * scale);
        int x = _anchor.X - r.Width / 2;
        int y = _anchor.Y + gap;
        _placedAbove = y + r.Height > work.Bottom;
        if (_placedAbove) y = Math.Max(work.Top, _anchor.Y - gap - r.Height);
        x = Math.Clamp(x, work.Left + 8, Math.Max(work.Left + 8, work.Right - r.Width - 8));
        y = Math.Clamp(y, work.Top + 8, Math.Max(work.Top + 8, work.Bottom - r.Height - 8));
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>
    /// When the window changes height (the translation arrived): above the selection it grows upward,
    /// so it never covers the text; below it, it moves up only if it would leave the screen.
    /// </summary>
    private void KeepOnScreen()
    {
        if (!IsVisible || _hwnd == IntPtr.Zero || !Native.GetWindowRect(_hwnd, out var r)) return;
        var work = WindowInfo.WorkAreaAt(new Native.POINT(r.Left + r.Width / 2, r.Top + 10));
        int y = r.Top;
        if (_placedAbove) y = _anchor.Y - (int)(18 * WindowInfo.ScaleAt(_anchor)) - r.Height;
        if (y + r.Height > work.Bottom - 8) y = work.Bottom - 8 - r.Height;
        y = Math.Max(work.Top + 8, y);
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
        _translatedText = text;
        UpdateTranslateButton();
        SetStatus("");
        SetBusy(true);

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
        finally
        {
            if (_cts == cts) SetBusy(false);
        }
    }

    private void ShowResult(TranslationResult result, bool usedFallback, string requestedTarget)
    {
        _last = result;
        _resultSection.Visibility = Visibility.Visible;
        _resultBox.Text = result.Translation;
        _resultBox.FlowDirection = Languages.IsRightToLeft(result.TargetLanguage) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _resultFrame.FlowDirection = _resultBox.FlowDirection;

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
        _resultSection.Visibility = Visibility.Collapsed;
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
        _status.SetResourceReference(TextBlock.ForegroundProperty, error ? "Adawat.Error" : "Adawat.Muted");
        UpdateSections();
    }

    /// <summary>
    /// Shows only what has something to show: the hairline and the translation once there is one
    /// (or one is on its way), the footer when there is a message or the Google link.
    /// </summary>
    private void UpdateSections()
    {
        bool result = _resultSection.Visibility == Visibility.Visible;
        _divider.Visibility = result || _busy ? Visibility.Visible : Visibility.Collapsed;
        _footer.Visibility = _status.Text.Length > 0 || T.ShowOpenInGoogle ? Visibility.Visible : Visibility.Collapsed;
        _footer.Margin = new Thickness(18, result || _busy ? 10 : 0, 18, 12);
    }

    /// <summary>The thin bar sliding along the hairline while a translation is on its way.</summary>
    private void SetBusy(bool busy)
    {
        if (_busy == busy) return;
        _busy = busy;
        var move = (TranslateTransform)_progressBar.RenderTransform;
        if (busy)
        {
            _progressBar.Visibility = Visibility.Visible;
            double width = Math.Max(200, ActualWidth - 36);
            var slide = new System.Windows.Media.Animation.DoubleAnimation(-_progressBar.Width, width, TimeSpan.FromMilliseconds(900))
            {
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut },
                BeginTime = TimeSpan.FromMilliseconds(150), // quick translations never show it
            };
            move.BeginAnimation(TranslateTransform.XProperty, slide);
        }
        else
        {
            move.BeginAnimation(TranslateTransform.XProperty, null);
            _progressBar.Visibility = Visibility.Collapsed;
        }
        UpdateSections();
    }

    /// <summary>"Translate" only shows while the text has changed since it was last translated.</summary>
    private void UpdateTranslateButton()
    {
        var text = _sourceBox.Text.Trim();
        _translateButton.Visibility = text.Length > 0 && text != _translatedText ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The window is exactly as tall as its content (up to the screen height).</summary>
    private void FitHeight(FrameworkElement content)
    {
        double height = Math.Ceiling(content.ActualHeight);
        if (height <= 0) return;
        height = Math.Min(MaxHeight, height);
        if (Math.Abs(Height - height) > 0.5) Height = height;
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
