using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Adawat.Core;
using Adawat.KeepAwake;
using Adawat.Ocr;
using Adawat.Translator;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using Image = System.Windows.Controls.Image;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace Adawat.UI;

/// <summary>The settings window: one page per tool.</summary>
public sealed class SettingsWindow : FluentWindow
{
    private readonly IAppHost _host;
    private readonly ListBox _nav = new();
    private readonly ScrollViewer _scroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly List<(string Title, SymbolRegular Icon, Func<UIElement> Build)> _pages;

    private AppSettings S => _host.Settings;

    public SettingsWindow(IAppHost host, string? startPage = null)
    {
        _host = host;
        Title = "Adawat Settings";
        Icon = AppIcon.ImageSource;
        Width = 1000;
        Height = 740;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowBackdropType = ThemeHelper.GlassSupported ? WindowBackdropType.Acrylic : WindowBackdropType.Mica;
        WindowCornerPreference = WindowCornerPreference.Round;
        ExtendsContentIntoTitleBar = true;
        FontFamily = new FontFamily(Ui.FontFamilyName);

        _pages = new()
        {
            ("General", SymbolRegular.Home24, BuildGeneral),
            ("Translator", SymbolRegular.Translate24, BuildTranslator),
            ("Text from screen", SymbolRegular.ScanText24, BuildOcr),
            ("Touchpad gestures", SymbolRegular.SwipeRight24, BuildGestures),
            ("Picture zoom", SymbolRegular.ImageSearch24, BuildImageZoom),
            ("Change keys", SymbolRegular.Keyboard24, BuildKeyChanges),
            ("Stay awake", SymbolRegular.WeatherMoonOff24, BuildKeepAwake),
            ("Quick note", SymbolRegular.Note24, BuildQuickNote),
            ("History", SymbolRegular.History24, BuildHistory),
            ("About", SymbolRegular.Info24, BuildAbout),
        };

        var titleBar = new TitleBar
        {
            Title = "Adawat",
            Icon = new ImageIcon { Source = AppIcon.ImageSource },
        };

        foreach (var (title, icon, _) in _pages)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 6, 4, 6) };
            row.Children.Add(Ui.Icon(icon, 18));
            var label = Ui.Text(title, 14);
            label.Margin = new Thickness(12, 0, 0, 0);
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            _nav.Items.Add(new ListBoxItem { Content = row, Margin = new Thickness(0, 1, 0, 1) });
        }
        _nav.Margin = new Thickness(12, 8, 8, 12);
        _nav.Width = 230;
        _nav.Background = Brushes.Transparent;
        _nav.BorderThickness = new Thickness(0);
        _nav.SelectionChanged += (_, _) => ShowPage(_nav.SelectedIndex);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_nav, 0);
        Grid.SetColumn(_scroller, 1);
        body.Children.Add(_nav);
        body.Children.Add(_scroller);

        var root = new Grid();
        // The accent color over the frosted glass, so the chosen color shows on the whole window.
        root.SetResourceReference(Panel.BackgroundProperty, "Adawat.WindowTint");
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(body, 1);
        root.Children.Add(titleBar);
        root.Children.Add(body);
        Content = root;

        void Reglaze()
        {
            if (ThemeHelper.GlassSupported) WindowBackdrop.ApplyBackdrop(this, WindowBackdropType.Acrylic);
        }
        ThemeHelper.Changed += Reglaze;
        Closed += (_, _) => ThemeHelper.Changed -= Reglaze;

        int index = Math.Max(0, _pages.FindIndex(p => string.Equals(p.Title, startPage, StringComparison.OrdinalIgnoreCase)));
        _nav.SelectedIndex = index;
    }

    public void ShowPage(string title)
    {
        int index = _pages.FindIndex(p => string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _nav.SelectedIndex = index;
    }

    private void ShowPage(int index)
    {
        if (index < 0 || index >= _pages.Count) return;
        var page = new StackPanel { Margin = new Thickness(24, 8, 28, 28), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Stretch };
        var heading = Ui.Text(_pages[index].Title, 28, weight: FontWeights.SemiBold);
        heading.Margin = new Thickness(2, 0, 0, 12);
        page.Children.Add(heading);
        page.Children.Add(_pages[index].Build());
        _scroller.Content = page;
        _scroller.ScrollToTop();
    }

    private void Changed() => _host.SettingsChanged();

    // ---- Pages ---------------------------------------------------------------------------

    private UIElement BuildGeneral()
    {
        var p = new StackPanel();

        var tips = new StackPanel();
        tips.Children.Add(Ui.Text("Adawat runs quietly in the system tray, next to the clock. Click its icon to open these settings; right-click it for the translator and more.", 13));
        foreach (var line in new[]
                 {
                     $"{S.Translator.TranslateHotkey}  —  translate the selected text in any app",
                     $"{S.Ocr.Hotkey}  —  read text from any part of the screen (pictures, videos, scanned PDFs)",
                     $"{S.Translator.OpenBoxHotkey}  —  open the translator box",
                     "Select text with the mouse  —  a small translate button appears next to it",
                     "Two-finger swipe left/right  —  back/forward;  pinch  —  zoom",
                     $"Rest the pointer on a picture  —  see it big;  {S.ImageZoom.SaveHotkey} saves it to Downloads, {S.ImageZoom.CopyHotkey} copies it",
                     $"{S.QuickNote.Hotkey}  —  open the quick note box (notes are saved as you type)",
                     "Right-click the tray icon → Stay awake  —  keep the PC working with the lid closed for a while",
                 })
        {
            var t = Ui.Text("•  " + line, 13, secondary: true);
            t.Margin = new Thickness(0, 6, 0, 0);
            tips.Children.Add(t);
        }
        var quick = Ui.Card(SymbolRegular.LightbulbFilament24, "Quick start", null, null);
        quick.Header = tips;
        p.Children.Add(quick);

        p.Children.Add(Ui.SectionTitle("Startup"));
        p.Children.Add(Ui.Card(SymbolRegular.Power24, "Start with Windows", "Adawat opens automatically when you sign in.",
            Ui.Toggle(S.StartWithWindows, v => { S.StartWithWindows = v; Changed(); })));

        ToggleSwitch? elevatedToggle = null;
        elevatedToggle = Ui.Toggle(S.RunElevated, v =>
        {
            if (v == S.RunElevated) return;
            if (!_host.SetRunElevated(v))
            {
                elevatedToggle!.IsChecked = S.RunElevated;
                System.Windows.MessageBox.Show(this, "The setting was not changed (the Windows permission prompt was cancelled or failed).",
                    "Adawat", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        });
        p.Children.Add(Ui.Card(SymbolRegular.ShieldKeyhole24, "Run as administrator",
            "Needed for the tools to work inside apps that run as administrator (Task Manager, installers, admin terminals). " +
            "Windows asks for permission once; after that Adawat starts this way without asking. " +
            $"Right now Adawat is running {(StartupManager.IsElevated ? "as administrator" : "normally")}.",
            elevatedToggle));

        p.Children.Add(Ui.SectionTitle("Appearance"));
        p.Children.Add(Ui.Card(SymbolRegular.DarkTheme24, "Theme", "For the translator, the picture preview and this window.",
            EnumCombo(S.Theme, new Dictionary<ThemeMode, string>
            {
                [ThemeMode.Light] = "Light",
                [ThemeMode.Dark] = "Dark",
                [ThemeMode.System] = "Same as Windows",
            }, v => { S.Theme = v; ThemeHelper.Set(S.Theme, S.Accent); Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.Color24, "Accent color", "Buttons, links and highlights.",
            AccentPicker()));

        return p;
    }

    private UIElement BuildTranslator()
    {
        var t = S.Translator;
        var p = new StackPanel();

        p.Children.Add(Ui.Card(SymbolRegular.Translate24, "Translator", "Turn the whole translator tool on or off.",
            Ui.Toggle(t.Enabled, v => { t.Enabled = v; Changed(); })));

        p.Children.Add(Ui.SectionTitle("Languages"));
        p.Children.Add(Ui.Card(SymbolRegular.LocalLanguage24, "Translate from", "\"Detect language\" works for English, Spanish and everything else.",
            LanguageCombo(t.SourceLanguage, includeAuto: true, includeNone: false, v => { t.SourceLanguage = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.LocalLanguage24, "Translate to", null,
            LanguageCombo(t.TargetLanguage, includeAuto: false, includeNone: false, v => { t.TargetLanguage = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.ArrowRepeatAll24, "If the text is already in that language, translate to",
            "For example: you select Arabic text while the target is Arabic.",
            LanguageCombo(t.FallbackTargetLanguage, includeAuto: false, includeNone: true, v => { t.FallbackTargetLanguage = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.TextAddSpaceAfter24, "Also show a second translation", "Shows the text in one more language under the main translation.",
            LanguageCombo(t.SecondTargetLanguage, includeAuto: false, includeNone: true, v => { t.SecondTargetLanguage = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.Star24, "Favorite languages", "Shown at the top of the language lists. Codes separated by commas, e.g. en, es, ar.",
            CommaListBox(t.FavoriteLanguages, v => { t.FavoriteLanguages = v; Changed(); })));

        p.Children.Add(Ui.SectionTitle("How to start it"));
        p.Children.Add(HotkeyCard(SymbolRegular.Keyboard24, "Translate selected text", "Select text anywhere, then press this shortcut. With nothing selected, it opens the translator box.",
            "translate", t.TranslateHotkey, v => { t.TranslateHotkey = v; Changed(); }));
        p.Children.Add(HotkeyCard(SymbolRegular.Keyboard24, "Open the translator box", "An empty box to type or paste text, like the browser extension's popup.",
            "openbox", t.OpenBoxHotkey, v => { t.OpenBoxHotkey = v; Changed(); }));
        p.Children.Add(Ui.Card(SymbolRegular.CursorClick24, "When I select text with the mouse", null,
            EnumCombo(t.OnSelection, new Dictionary<SelectionBehavior, string>
            {
                [SelectionBehavior.ShowIcon] = "Show the translate button",
                [SelectionBehavior.TranslateInstantly] = "Translate right away",
                [SelectionBehavior.DoNothing] = "Do nothing",
            }, v => { t.OnSelection = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.KeyboardShift24, "Only while holding a key", "React to mouse selections only while this key is held down.",
            EnumCombo(t.SelectionModifier, new Dictionary<SelectionModifier, string>
            {
                [SelectionModifier.None] = "No key needed",
                [SelectionModifier.Alt] = "Alt",
                [SelectionModifier.Ctrl] = "Ctrl",
                [SelectionModifier.Shift] = "Shift",
            }, v => { t.SelectionModifier = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.TapSingle24, "Only when selecting text", "Ignore drags that start on something that is not text (files, windows, pictures).",
            Ui.Toggle(t.OnlyOverText, v => { t.OnlyOverText = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.CursorHover24, "Keep the translate button while the text is selected",
            "It goes away when you click somewhere else, type, scroll or switch apps. Turn off to hide it after a few seconds instead.",
            Ui.Toggle(t.KeepIconWhileSelected, v => { t.KeepIconWhileSelected = v; Changed(); ShowPage("Translator"); })));
        if (!t.KeepIconWhileSelected)
        {
            p.Children.Add(Ui.Card(SymbolRegular.Timer24, "Hide the translate button after", null,
                SliderRow(1, 10, t.IconSeconds, v => $"{v:0} s", v => { t.IconSeconds = Math.Round(v); Changed(); })));
        }

        p.Children.Add(Ui.SectionTitle("Look of the translator"));
        string lookNote = ThemeHelper.GlassSupported
            ? "Glass: frosted, see-through glass with a light veil of the colors below. Color: solid colors, nothing shows through."
            : "Glass needs Windows 11 (version 22H2 or later); here the translator uses solid colors.";
        p.Children.Add(Ui.Card(SymbolRegular.Blur24, "Style", lookNote,
            EnumCombo(t.Look, new Dictionary<TranslatorLook, string>
            {
                [TranslatorLook.Glass] = "Glass",
                [TranslatorLook.Color] = "Color",
            }, v => { t.Look = v; Changed(); ShowPage("Translator"); })));
        p.Children.Add(Ui.Card(SymbolRegular.Color24, "Colors", "One color, or two colors that flow into each other. \"App color\" follows the accent color in General.",
            TranslatorColorPicker()));
        if (t.Look == TranslatorLook.Glass && ThemeHelper.GlassSupported)
        {
            p.Children.Add(Ui.Card(SymbolRegular.Eye24, "How see-through",
                "Higher shows more of what is behind the window. If Windows' \"Transparency effects\" are off (Settings → Personalization → Colors), " +
                "or the window is pinned and you work in another app, Windows shows the glass as a plain color.",
                SliderRow(1, 100, t.GlassClarity, v => $"{v:0}", v => { t.GlassClarity = (int)Math.Round(v); Changed(); })));
        }

        p.Children.Add(Ui.SectionTitle("Translation window"));
        p.Children.Add(Ui.Card(SymbolRegular.TextFont24, "Text size", null,
            SliderRow(12, 26, t.FontSize, v => $"{v:0}", v => { t.FontSize = Math.Round(v); Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.ArrowAutofitWidth24, "Window width", null,
            SliderRow(440, 900, t.PopupWidth, v => $"{v:0}", v => { t.PopupWidth = Math.Round(v / 10) * 10; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.Dismiss24, "Close when clicking outside", "Pin the window (📌) to keep it open.",
            Ui.Toggle(t.CloseOnClickOutside, v => { t.CloseOnClickOutside = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.Book24, "Show dictionary", "Meanings by word type for single words.",
            Ui.Toggle(t.ShowDictionary, v => { t.ShowDictionary = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.TextT24, "Show pronunciation", "How the translation is read, in Latin letters.",
            Ui.Toggle(t.ShowTransliteration, v => { t.ShowTransliteration = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.Copy24, "Copy the translation automatically", null,
            Ui.Toggle(t.AutoCopyTranslation, v => { t.AutoCopyTranslation = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.Open24, "Show \"Open in Google Translate\"", "The link at the bottom of the translation window.",
            Ui.Toggle(t.ShowOpenInGoogle, v => { t.ShowOpenInGoogle = v; Changed(); })));

        p.Children.Add(Ui.SectionTitle("Advanced"));
        p.Children.Add(Ui.Card(SymbolRegular.Clipboard24, "How to read the selected text", "Automatic copies the selection with Ctrl+C and restores your clipboard; if that finds nothing it uses Windows accessibility.",
            EnumCombo(t.CaptureMethod, new Dictionary<CaptureMethod, string>
            {
                [CaptureMethod.Automatic] = "Automatic (recommended)",
                [CaptureMethod.ClipboardOnly] = "Copy (Ctrl+C) only",
                [CaptureMethod.AccessibilityOnly] = "Accessibility only (never touches the clipboard)",
            }, v => { t.CaptureMethod = v; Changed(); })));
        p.Children.Add(AppListCard("Apps where the translator is off", "One program per line, e.g. game.exe", t.DisabledApps, v => { t.DisabledApps = v; Changed(); }));
        p.Children.Add(AppListCard("Apps where Ctrl+C is never sent", "In terminals Ctrl+C stops the running program, so Adawat reads the selection another way there.", t.NoCopyShortcutApps, v => { t.NoCopyShortcutApps = v; Changed(); }));

        var testResult = Ui.Text("", 12, secondary: true);
        testResult.Margin = new Thickness(0, 0, 12, 0);
        testResult.VerticalAlignment = VerticalAlignment.Center;
        var testRow = new StackPanel { Orientation = Orientation.Horizontal };
        testRow.Children.Add(testResult);
        testRow.Children.Add(Ui.Button("Test", async () =>
        {
            testResult.Text = "Testing…";
            testResult.Text = await _host.TestTranslationAsync();
        }));
        p.Children.Add(Ui.Card(SymbolRegular.Globe24, "Google Translate connection", "Uses the free Google Translate service. No account or key needed.", testRow));
        p.Children.Add(Ui.Card(SymbolRegular.BrainCircuit24, "Translation service",
            "Same as translate.google.com (recommended): the service the Google Translate website uses, so the translations match it, " +
            "and long text is translated as a whole. If it does not answer, Adawat uses the classic service on its own. " +
            "Classic: the older address used by browser extensions; its translations can be weaker.",
            EnumCombo(t.Engine, new Dictionary<TranslationEngine, string>
            {
                [TranslationEngine.Website] = "Same as translate.google.com",
                [TranslationEngine.Classic] = "Classic",
            }, v => { t.Engine = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.TextWrap24, "Join lines broken in the middle of sentences",
            "Text from PDFs and narrow web columns has a line break at the end of every line. Joined, Google sees whole sentences " +
            "and translates much better. Paragraphs and lists stay as they are.",
            Ui.Toggle(t.JoinBrokenLines, v => { t.JoinBrokenLines = v; Changed(); })));

        return p;
    }

    private UIElement BuildOcr()
    {
        var o = S.Ocr;
        var p = new StackPanel();
        p.Children.Add(Ui.Card(SymbolRegular.ScanText24, "Text from screen", "Read text from pictures, videos, scanned PDFs or anything else on the screen.",
            Ui.Toggle(o.Enabled, v => { o.Enabled = v; Changed(); })));
        p.Children.Add(HotkeyCard(SymbolRegular.Keyboard24, "Shortcut", "Press it, then drag around the text.",
            "ocr", o.Hotkey, v => { o.Hotkey = v; Changed(); }));

        var installed = OcrService.InstalledLanguages();
        var combo = new ComboBox { MinWidth = 220 };
        combo.Items.Add(new ComboBoxItem { Content = "Automatic", Tag = "auto" });
        foreach (var lang in installed) combo.Items.Add(new ComboBoxItem { Content = lang.Name, Tag = lang.Tag });
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == o.Language) ?? combo.Items[0];
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem item) { o.Language = (string)item.Tag; Changed(); }
        };
        var names = installed.Count == 0 ? "none" : string.Join(", ", installed.Select(l => l.Name));
        p.Children.Add(Ui.Card(SymbolRegular.LocalLanguage24, "Text language",
            $"Installed in Windows: {names}. Automatic tries them all and keeps the best result.", combo));
        p.Children.Add(Ui.Card(SymbolRegular.Add24, "Add a language for reading text",
            "Windows reads a language only after you add it (Settings → Time & language → Language & region → Add a language, " +
            "with \"Optical character recognition\"). Add Arabic or Spanish there to read them from pictures.",
            Ui.Button("Open language settings", () => OpenUri("ms-settings:regionlanguage"))));

        p.Children.Add(Ui.SectionTitle("After reading the text"));
        p.Children.Add(Ui.Card(SymbolRegular.Translate24, "Translate it right away", null,
            Ui.Toggle(o.TranslateAfterCapture, v => { o.TranslateAfterCapture = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.Copy24, "Copy it to the clipboard", null,
            Ui.Toggle(o.CopyAfterCapture, v => { o.CopyAfterCapture = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.TextWrap24, "Keep line breaks", "Off joins all lines into one paragraph.",
            Ui.Toggle(o.KeepLineBreaks, v => { o.KeepLineBreaks = v; Changed(); })));

        p.Children.Add(Ui.SectionTitle("File Explorer"));
        p.Children.Add(Ui.Card(SymbolRegular.Image24, "Add \"Extract text\" to pictures' right-click menu",
            "Right-click a picture → Show more options → Extract text (Adawat).",
            Ui.Toggle(o.ImageContextMenu, v => { o.ImageContextMenu = v; Changed(); })));
        return p;
    }

    private UIElement BuildGestures()
    {
        var g = S.Gestures;
        var p = new StackPanel();

        string status = _host.TouchpadPresent
            ? "A precision touchpad was found."
            : "No precision touchpad was found. Gestures need a Windows Precision Touchpad (most laptops from 2016 on).";
        p.Children.Add(Ui.Card(SymbolRegular.SwipeRight24, "Touchpad gestures", status,
            Ui.Toggle(g.Enabled, v => { g.Enabled = v; Changed(); })));

        p.Children.Add(Ui.SectionTitle("Swipe for back and forward"));
        p.Children.Add(Ui.Card(SymbolRegular.ArrowHookUpLeft24, "Swipe to go back / forward",
            "Swipe right to go back and left to go forward, like in Chrome. Works in File Explorer, Settings, Photos, VS Code and most apps.",
            Ui.Toggle(g.SwipeEnabled, v => { g.SwipeEnabled = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.HandLeft24, "Number of fingers",
            "Two fingers is what Chrome uses. If you pick three or four, turn off the Windows gesture for that number of fingers " +
            "(Settings → Bluetooth & devices → Touchpad).",
            EnumCombo(g.SwipeFingers, new Dictionary<int, string> { [2] = "Two fingers", [3] = "Three fingers", [4] = "Four fingers" },
                v => { g.SwipeFingers = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.Gauge24, "Swipe sensitivity", "Higher means a shorter swipe is enough.",
            SliderRow(1, 100, g.SwipeSensitivity, v => $"{v:0}", v => { g.SwipeSensitivity = (int)Math.Round(v); Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.ArrowSwap24, "Reverse swipe direction", null,
            Ui.Toggle(g.InvertSwipe, v => { g.InvertSwipe = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.ArrowLeft24, "How to go back",
            "The mouse back/forward buttons work in the most apps. Some apps only react to Alt + arrow keys.",
            EnumCombo(g.SwipeAction, new Dictionary<SwipeAction, string>
            {
                [SwipeAction.MouseButtons] = "Mouse back/forward buttons",
                [SwipeAction.AltArrows] = "Alt + ← / →",
                [SwipeAction.BrowserKeys] = "Browser back/forward keys",
            }, v => { g.SwipeAction = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.Prohibited24, "Only when there is somewhere to go",
            "If the app's own back (or forward) button is greyed out, the swipe does nothing and no arrow shows. " +
            "Apps without a visible back button keep working as before.",
            Ui.Toggle(g.SwipeOnlyWhenPossible, v => { g.SwipeOnlyWhenPossible = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.ArrowCircleLeft24, "Show the arrow while swiping", null,
            Ui.Toggle(g.ShowSwipeArrow, v => { g.ShowSwipeArrow = v; Changed(); })));
        p.Children.Add(AppListCard("Apps where swiping does nothing", "Browsers already have this gesture, so they are listed by default.",
            g.SwipeDisabledApps, v => { g.SwipeDisabledApps = v; Changed(); }));

        p.Children.Add(Ui.SectionTitle("Pinch to zoom"));
        p.Children.Add(Ui.Card(SymbolRegular.ZoomIn24, "Pinch to zoom",
            "Spread two fingers to zoom in and pinch them to zoom back out.",
            Ui.Toggle(g.PinchEnabled, v => { g.PinchEnabled = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.ZoomFit24, "What pinching does",
            "Screen zoom magnifies what you see around the pointer, like pinching in Chrome: nothing in the app changes size, " +
            "and moving the pointer moves around the zoomed view. The other options ask the app itself to zoom (icon or text size).",
            EnumCombo(g.PinchAction, new Dictionary<PinchAction, string>
            {
                [PinchAction.ScreenZoom] = "Screen zoom, like Chrome (recommended)",
                [PinchAction.CtrlWheel] = "App zoom: Ctrl + mouse wheel",
                [PinchAction.CtrlPlusMinus] = "App zoom: Ctrl + Plus / Minus",
            }, v => { g.PinchAction = v; Changed(); ShowPage("Touchpad gestures"); })));

        if (g.PinchAction == PinchAction.ScreenZoom)
        {
            p.Children.Add(Ui.Card(SymbolRegular.Gauge24, "Zoom speed",
                "How fast the screen zooms as your fingers spread. Lower is slower and easier to control; 1 is very slow.",
                SliderRow(1, 100, g.ScreenZoomSpeed, v => $"{v:0}", v => { g.ScreenZoomSpeed = (int)Math.Round(v); Changed(); })));
            p.Children.Add(Ui.Card(SymbolRegular.ZoomIn24, "Maximum zoom", "How far you can zoom in, from a little (1.25x) to a lot (10x).",
                SliderRow(1.25, 10, g.MaxScreenZoom, v => $"{Math.Round(v * 4) / 4:0.##}x", v => { g.MaxScreenZoom = Math.Round(v * 4) / 4; Changed(); })));
            p.Children.Add(Ui.Card(SymbolRegular.Eye24, "Picture",
                "Smooth edges (recommended) blends the enlarged pixels, like Windows Magnifier, and stays steady while zooming. " +
                "Blocky shows every screen pixel as a square, which flickers while the zoom changes.",
                EnumCombo(g.SmoothZoomEdges, new Dictionary<bool, string> { [true] = "Smooth edges", [false] = "Blocky pixels" },
                    v => { g.SmoothZoomEdges = v; Changed(); })));
            p.Children.Add(Ui.Card(SymbolRegular.ZoomFit24, "Settle on whole zoom levels",
                "When you lift your fingers close to 2x, 3x…, the zoom settles exactly there, where the picture is sharpest.",
                Ui.Toggle(g.SnapZoomLevels, v => { g.SnapZoomLevels = v; Changed(); })));
            p.Children.Add(Ui.Card(SymbolRegular.ArrowMove24, "Moving around while zoomed",
                "While zoomed, slide two fingers in any direction to move the zoomed view, like in Chrome. " +
                "Back/forward swipes pause until you zoom out.",
                SliderRow(1, 100, g.PanSpeed, v => $"{v:0}", v => { g.PanSpeed = (int)Math.Round(v); Changed(); })));
            p.Children.Add(Ui.Card(SymbolRegular.CursorHover24, "When the mouse pointer reaches the edge",
                "Stay (recommended): the pointer stops at the edge of the zoomed view and the view only moves with two fingers. " +
                "Move the view: pushing the pointer against the edge slides the zoomed view along.",
                EnumCombo(g.PointerMovesZoom, new Dictionary<bool, string> { [false] = "The view stays put", [true] = "Move the view" },
                    v => { g.PointerMovesZoom = v; Changed(); })));
            p.Children.Add(Ui.Card(SymbolRegular.ArrowSortDown24, "Keep scrolling at the edge",
                "Like Chrome: once the zoomed view reaches the edge of the screen, keep sliding and the page itself scrolls on. " +
                "Off: the view stops at the edge.",
                Ui.Toggle(g.ScrollPastZoomEdges, v => { g.ScrollPastZoomEdges = v; Changed(); })));
            p.Children.Add(Ui.Card(SymbolRegular.ArrowSwap24, "Reverse moving direction", "Off: the picture follows your fingers, like Chrome.",
                Ui.Toggle(g.InvertPan, v => { g.InvertPan = v; Changed(); })));

            p.Children.Add(Ui.Card(SymbolRegular.Prohibited24, "Stop Windows' pinch from also resizing apps",
                "Windows' own pinch makes File Explorer and other apps change icon or text size. While your fingers are on the " +
                "touchpad over an app where Adawat zooms, Windows' pinch is paused for that moment only. Your Windows setting is " +
                "never changed, and in the apps listed under \"Apps where pinching does nothing\" Windows' pinch works as usual.",
                Ui.Toggle(g.TurnOffWindowsPinch, v => { g.TurnOffWindowsPinch = v; Changed(); })));
            p.Children.Add(Ui.Card(SymbolRegular.ArrowReset24, "Back to normal size", "Pinch your fingers together, or use this button.",
                Ui.Button("Reset zoom", () => _host.ResetZoom())));
        }
        else
        {
            p.Children.Add(Ui.Card(SymbolRegular.Gauge24, "Pinch sensitivity", "Higher means a smaller pinch makes one zoom step.",
                SliderRow(1, 100, g.PinchSensitivity, v => $"{v:0}", v => { g.PinchSensitivity = (int)Math.Round(v); Changed(); })));
            p.Children.Add(Ui.Card(SymbolRegular.Checkmark24, "Skip apps that Windows already zooms",
                "Windows itself zooms some apps on pinch. Adawat notices this and stays out of the way so you don't zoom twice.",
                Ui.Toggle(g.SkipNativePinchApps, v => { g.SkipNativePinchApps = v; Changed(); })));
        }
        p.Children.Add(Ui.Card(SymbolRegular.ArrowSwap24, "Reverse zoom direction", null,
            Ui.Toggle(g.InvertPinch, v => { g.InvertPinch = v; Changed(); })));
        p.Children.Add(AppListCard("Apps where pinching does nothing",
            "Adawat leaves the pinch to these apps (and to Windows), e.g. chrome.exe to keep Chrome's own zoom. One program per line.",
            g.PinchDisabledApps, v => { g.PinchDisabledApps = v; Changed(); }));

        p.Children.Add(Ui.SectionTitle("Other"));
        p.Children.Add(Ui.Card(SymbolRegular.Games24, "Pause gestures in full-screen apps", "Games, videos and presentations.",
            Ui.Toggle(g.DisableInFullscreen, v => { g.DisableInFullscreen = v; Changed(); })));
        return p;
    }

    private UIElement BuildImageZoom()
    {
        var z = S.ImageZoom;
        var p = new StackPanel();
        p.Children.Add(Ui.Card(SymbolRegular.ImageSearch24, "Picture zoom",
            "Rest the pointer on a picture in any app to see it big, like the Photo Zoom browser extension. Works in browsers, " +
            "File Explorer, the desktop, chat apps and more.",
            Ui.Toggle(z.Enabled, v => { z.Enabled = v; Changed(); })));

        p.Children.Add(Ui.SectionTitle("Shortcuts while a picture is open"));
        p.Children.Add(HotkeyCard(SymbolRegular.ArrowDownload24, "Save to Downloads",
            "Saves the full picture in your Downloads folder. Only active while the preview is open, so other apps keep this shortcut.",
            "imagesave", z.SaveHotkey, v => { z.SaveHotkey = v; Changed(); }));
        p.Children.Add(HotkeyCard(SymbolRegular.Copy24, "Copy to the clipboard", "Paste it into chats, documents or image editors.",
            "imagecopy", z.CopyHotkey, v => { z.CopyHotkey = v; Changed(); }));

        p.Children.Add(Ui.SectionTitle("When to open"));
        p.Children.Add(Ui.Card(SymbolRegular.Timer24, "Wait before opening", "How long the pointer rests on a picture first.",
            SliderRow(0, 1500, z.HoverDelayMs, v => $"{v / 1000:0.0} s", v => { z.HoverDelayMs = (int)(Math.Round(v / 50) * 50); Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.KeyboardShift24, "Only while holding a key", "Open pictures only while this key is held down.",
            EnumCombo(z.Modifier, new Dictionary<SelectionModifier, string>
            {
                [SelectionModifier.None] = "No key needed",
                [SelectionModifier.Alt] = "Alt",
                [SelectionModifier.Ctrl] = "Ctrl",
                [SelectionModifier.Shift] = "Shift",
            }, v => { z.Modifier = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.ResizeSmall24, "Ignore small pictures", "Icons and pictures smaller than this are left alone.",
            SliderRow(16, 200, z.MinSize, v => $"{v:0} px", v => { z.MinSize = (int)Math.Round(v); Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.ScreenSearch24, "Enlarge pictures from the screen",
            "Picture files and pictures that link to their full size open at full quality. For other pictures (most web pages) " +
            "Adawat can only enlarge what is on screen, which is less sharp. Turn off to open only full-quality pictures.",
            Ui.Toggle(z.EnlargeScreenPictures, v => { z.EnlargeScreenPictures = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.Games24, "Pause in full-screen apps", "Games, videos and presentations.",
            Ui.Toggle(z.DisableInFullscreen, v => { z.DisableInFullscreen = v; Changed(); })));
        p.Children.Add(AppListCard("Apps where picture zoom is off", "One program per line, e.g. photoshop.exe", z.DisabledApps, v => { z.DisabledApps = v; Changed(); }));
        return p;
    }

    private UIElement BuildKeepAwake()
    {
        var k = S.KeepAwake;
        var awake = _host.KeepAwake;
        var p = new StackPanel();

        var status = Ui.Text("", 12, secondary: true);
        var startButton = Ui.Button("Start", () => awake.StartWithSettings(), primary: true, icon: SymbolRegular.Play24);
        var stopButton = Ui.Button("Turn off", () => awake.Stop(), icon: SymbolRegular.Stop24);
        stopButton.Margin = new Thickness(8, 0, 0, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(startButton);
        buttons.Children.Add(stopButton);
        var main = Ui.Card(SymbolRegular.WeatherMoonOff24, "Stay awake",
            "Keep the PC working, also with the lid closed: downloads, uploads and programs go on and Wi-Fi stays connected. " +
            "When the time is up, it can sleep again.", buttons);
        if (main.Header is StackPanel header)
        {
            status.Margin = new Thickness(0, 6, 0, 0);
            header.Children.Add(status);
        }
        void Refresh()
        {
            status.Text = awake.Status;
            status.SetResourceReference(TextBlock.ForegroundProperty, awake.IsOn ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
            startButton.Content = awake.IsOn ? "Start again" : "Start";
            stopButton.IsEnabled = awake.IsOn;
        }
        Refresh();
        awake.Changed += Refresh;
        // The page is rebuilt when shown again; stop updating the old one.
        main.Unloaded += (_, _) => awake.Changed -= Refresh;
        main.Loaded += (_, _) =>
        {
            awake.Changed -= Refresh;
            awake.Changed += Refresh;
            Refresh();
        };
        p.Children.Add(main);

        var lengths = new Dictionary<int, string>();
        foreach (int minutes in new[] { 30, 60, 120, 180, 240, 480, 0 }) lengths[minutes] = KeepAwakeService.LengthName(minutes);
        lengths[KeepAwakeSettings.CustomLength] = "Another length…";
        lengths[KeepAwakeSettings.UntilClockTime] = "Until a time of day…";
        p.Children.Add(Ui.Card(SymbolRegular.Timer24, "How long", "Starting again (or picking another length while it is on) counts from now.",
            EnumCombo(k.Minutes, lengths, v =>
            {
                k.Minutes = v;
                Changed();
                if (awake.IsOn) awake.StartWithSettings();
                ShowPage("Stay awake");
            })));
        if (k.Minutes == KeepAwakeSettings.CustomLength)
        {
            p.Children.Add(Ui.Card(SymbolRegular.TimePicker24, "Length", "From 5 minutes to 12 hours.",
                SliderRow(5, 720, k.CustomMinutes, v => KeepAwakeService.Remaining(TimeSpan.FromMinutes(Math.Round(v / 5) * 5)),
                    v =>
                    {
                        k.CustomMinutes = (int)(Math.Round(v / 5) * 5);
                        Changed();
                        if (awake.IsOn) awake.StartWithSettings();
                    })));
        }
        else if (k.Minutes == KeepAwakeSettings.UntilClockTime)
        {
            var clock = new Wpf.Ui.Controls.TextBox { Text = k.UntilClock, MinWidth = 140, PlaceholderText = "18:30" };
            var hint = Ui.Text("", 12, secondary: true);
            hint.Margin = new Thickness(0, 4, 0, 0);
            void ShowEnd()
            {
                hint.Text = KeepAwakeService.TryParseClock(clock.Text, DateTime.Now, out var end)
                    ? $"Until {end:t}{(end.Date > DateTime.Today ? " tomorrow" : "")}"
                    : "Write the time like 18:30 or 6:30 PM";
            }
            clock.TextChanged += (_, _) => ShowEnd();
            clock.LostFocus += (_, _) =>
            {
                if (clock.Text == k.UntilClock) return;
                k.UntilClock = clock.Text.Trim();
                Changed();
                if (awake.IsOn && KeepAwakeService.TryParseClock(k.UntilClock, DateTime.Now, out _)) awake.StartWithSettings();
            };
            ShowEnd();
            var box = new StackPanel();
            box.Children.Add(clock);
            box.Children.Add(hint);
            p.Children.Add(Ui.Card(SymbolRegular.Clock24, "Until", "The next time the clock shows this time (today, or tomorrow if it has passed).", box));
        }

        p.Children.Add(Ui.SectionTitle("While staying awake"));
        p.Children.Add(Ui.Card(SymbolRegular.Laptop24, "Keep working with the lid closed",
            "Closing the lid does nothing while Stay awake is on (only the screen goes dark). Afterwards Windows' own " +
            "\"When I close the lid\" setting is put back exactly as it was.",
            Ui.Toggle(k.WhileLidClosed, v => { k.WhileLidClosed = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.WeatherSunny24, "Keep the screen on", "Off: the screen may turn off as usual; the PC keeps working.",
            Ui.Toggle(k.KeepScreenOn, v => { k.KeepScreenOn = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.WeatherMoon24, "Sleep when the time is up, if the lid is closed",
            "Otherwise the PC sleeps the usual way, after the time set in Windows.",
            Ui.Toggle(k.SleepWhenDoneIfLidClosed, v => { k.SleepWhenDoneIfLidClosed = v; Changed(); })));

        p.Children.Add(Ui.SectionTitle("Good to know"));
        p.Children.Add(Ui.Card(SymbolRegular.Info24, "Quick start from the tray",
            "Right-click Adawat's tray icon → Stay awake → 30 minutes, 1 hour, 2 hours, 4 hours or until you turn it off.", null));
        p.Children.Add(Ui.Card(SymbolRegular.Warning24, "Laptop bags",
            "A closed laptop that keeps working gets warm. Don't put it in a bag while Stay awake is on.", null));
        return p;
    }

    private UIElement BuildQuickNote()
    {
        var q = S.QuickNote;
        var p = new StackPanel();
        p.Children.Add(Ui.Card(SymbolRegular.Note24, "Quick note",
            "A small box to jot things down. Notes are saved as you type, in notes.json in the Adawat data folder.",
            Ui.Toggle(q.Enabled, v => { q.Enabled = v; Changed(); })));
        p.Children.Add(HotkeyCard(SymbolRegular.Keyboard24, "Shortcut", "Opens the note box. Press it again to hide the box.",
            "quicknote", q.Hotkey, v => { q.Hotkey = v; Changed(); }));

        p.Children.Add(Ui.SectionTitle("Behavior"));
        p.Children.Add(Ui.Card(SymbolRegular.Dismiss24, "Close when clicking elsewhere",
            "Off: the box stays open until you close it (Esc or the close button).",
            Ui.Toggle(q.CloseOnClickOutside, v => { q.CloseOnClickOutside = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.Pin24, "Keep on top",
            "The box stays above other windows and does not close when you click elsewhere. Same as the pin in the box.",
            Ui.Toggle(q.KeepOnTop, v => { q.KeepOnTop = v; Changed(); })));

        p.Children.Add(Ui.SectionTitle("Look"));
        p.Children.Add(Ui.Card(SymbolRegular.ArrowAutofitWidth24, "Tabs", "The strip with one tab per note.",
            EnumCombo(q.TabsSide, new Dictionary<TabSide, string>
            {
                [TabSide.Right] = "On the right",
                [TabSide.Left] = "On the left",
            }, v => { q.TabsSide = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.Color24, "Color", "\"App color\" follows the accent color in General.",
            ColorChoicePicker(() => q.Colors, v => q.Colors = v)));
        p.Children.Add(Ui.Card(SymbolRegular.TextFont24, "Text size", null,
            SliderRow(11, 30, q.FontSize, v => $"{v:0}", v => { q.FontSize = Math.Round(v); Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.TextFont24, "Font", null,
            EnumCombo(q.FontFamily, new Dictionary<string, string>
            {
                ["Segoe UI"] = "Segoe UI",
                ["Calibri"] = "Calibri",
                ["Arial"] = "Arial",
                ["Tahoma"] = "Tahoma",
                ["Times New Roman"] = "Times New Roman",
                ["Consolas"] = "Consolas",
            }, v => { q.FontFamily = v; Changed(); })));

        p.Children.Add(Ui.SectionTitle("Notes"));
        p.Children.Add(Ui.Card(SymbolRegular.FolderOpen24, "Open the notes folder",
            "notes.json is in the same folder as the settings and the history.",
            Ui.Button("Open folder", () =>
            {
                try { System.IO.Directory.CreateDirectory(AppPaths.DataFolder); } catch { }
                OpenUri(AppPaths.DataFolder);
            })));
        return p;
    }

    private UIElement BuildKeyChanges()
    {
        var k = S.KeyChanges;
        var p = new StackPanel();
        p.Children.Add(Ui.Card(SymbolRegular.Keyboard24, "Change keys",
            "Make one key act as another, in every app. For example, swap Left Ctrl and Left Alt: then copy is Alt + C, " +
            "paste is Alt + V, and the key next to the space bar does what Ctrl does.",
            Ui.Toggle(k.Enabled, v => { k.Enabled = v; Changed(); })));

        p.Children.Add(Ui.SectionTitle("Your key changes"));
        var list = new StackPanel();

        void Save()
        {
            Changed();
            Fill();
        }

        void Fill()
        {
            list.Children.Clear();
            if (k.Remaps.Count == 0)
            {
                var empty = Ui.Card(SymbolRegular.Info24, "No key changes yet",
                    "Add one below, or use \"Swap Ctrl and Alt\". Click a key box, then press the key on your keyboard.", null);
                list.Children.Add(empty);
            }
            foreach (var remap in k.Remaps.ToList())
            {
                var from = KeyPicker(remap.From, allowOff: false, v => { remap.From = v; Save(); });
                var to = KeyPicker(remap.To, allowOff: true, v => { remap.To = v; Save(); });
                var arrow = Ui.Icon(SymbolRegular.ArrowRight24, 16);
                arrow.Margin = new Thickness(10, 0, 10, 0);
                arrow.VerticalAlignment = VerticalAlignment.Center;
                var remove = Ui.IconButton(SymbolRegular.Delete24, "Remove this change", () => { k.Remaps.Remove(remap); Save(); });
                remove.Margin = new Thickness(12, 0, 0, 0);
                var toggle = Ui.Toggle(remap.Enabled, v => { remap.Enabled = v; Changed(); });
                toggle.Margin = new Thickness(0, 0, 16, 0);
                toggle.ToolTip = "Turn this change on or off";
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(toggle);
                row.Children.Add(from);
                row.Children.Add(arrow);
                row.Children.Add(to);
                row.Children.Add(remove);
                string title = remap.From == 0 ? "New key change" : $"{KeyChanges.KeyNames.Name(remap.From)} works as {KeyChanges.KeyNames.Name(remap.To)}";
                list.Children.Add(Ui.Card(SymbolRegular.ArrowSwap24, title, null, row));
                if (remap.From is 0xA4 or 0xA5 && remap.To != 0xA4 && remap.To != 0xA5) list.Children.Add(KeepAltCard(remap));
            }
        }
        Fill();
        p.Children.Add(list);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var add = Ui.Button("Add a key change", () =>
        {
            k.Remaps.Add(new KeyRemap { From = 0, To = 0, Enabled = true });
            Fill();
        }, primary: true, icon: SymbolRegular.Add24);
        var swap = Ui.Button("Swap Ctrl and Alt", () =>
        {
            k.Remaps.RemoveAll(r => r.From is 0xA2 or 0xA4 or 0xA3 or 0xA5);
            k.Remaps.Add(new KeyRemap { From = 0xA4, To = 0xA2, Keep = KeyRemap.DefaultAltKeep() }); // Left Alt  -> Left Ctrl
            k.Remaps.Add(new KeyRemap { From = 0xA2, To = 0xA4 }); // Left Ctrl -> Left Alt
            k.Remaps.Add(new KeyRemap { From = 0xA5, To = 0xA3, Keep = KeyRemap.DefaultAltKeep() }); // Right Alt  -> Right Ctrl
            k.Remaps.Add(new KeyRemap { From = 0xA3, To = 0xA5 }); // Right Ctrl -> Right Alt
            k.Enabled = true;
            Save();
            ShowPage("Change keys");
        }, icon: SymbolRegular.ArrowSwap24);
        swap.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(add);
        buttons.Children.Add(swap);
        p.Children.Add(buttons);

        p.Children.Add(Ui.SectionTitle("Good to know"));
        p.Children.Add(Ui.Card(SymbolRegular.Info24, "What changes",
            "Only the key itself changes, so every shortcut with it follows: with Left Alt working as Left Ctrl, Alt + C copies, " +
            "Alt + Z undoes and Alt + Tab becomes Ctrl + Tab. Adawat's own shortcuts (like " + S.Translator.TranslateHotkey + ") follow too. " +
            "Ctrl + Alt + Delete and Windows + L always keep their meaning.", null));
        p.Children.Add(Ui.Card(SymbolRegular.ShieldKeyhole24, "Apps that run as administrator",
            "Windows only lets the changes reach apps running as administrator (Task Manager, installers) when Adawat runs as administrator too. Turn it on in General.", null));
        p.Children.Add(AppListCard("Apps where keys are not changed", "Games or remote desktop, for example. One program per line, e.g. mstsc.exe",
            k.DisabledApps, v => { k.DisabledApps = v; Changed(); }));
        return p;
    }

    /// <summary>The Alt shortcuts that can keep working as Alt shortcuts while Alt works as another key.</summary>
    private static readonly (string Label, string Hint, int[] Keys)[] AltShortcuts =
    {
        ("Alt + Tab", "switch windows", new[] { 0x09 }),
        ("Alt + F4", "close the window", new[] { 0x73 }),
        ("Alt + Esc", "next window", new[] { 0x1B }),
        ("Alt + Space", "window menu", new[] { 0x20 }),
        ("Alt + Shift", "switch keyboard language (Ctrl + Shift shortcuts then need the other Ctrl)", new[] { 0xA0, 0xA1 }),
        ("Alt + ← / →", "back and forward", new[] { 0x25, 0x27 }),
        ("Alt + Enter", "properties, full screen", new[] { 0x0D }),
        ("Alt + 0…9", "number shortcuts, such as Adawat's Alt + 1", new[] { 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39 }),
        ("Alt + letters", "menus and app shortcuts (then Alt + C no longer copies)", Enumerable.Range(0x41, 26).ToArray()),
    };

    /// <summary>Checkboxes for the Alt shortcuts that stay Alt shortcuts (Alt + Tab…) under a rule that changes Alt.</summary>
    private CardControl KeepAltCard(KeyRemap remap)
    {
        var boxes = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (label, hint, keys) in AltShortcuts)
        {
            var box = new CheckBox
            {
                Content = label,
                ToolTip = hint,
                IsChecked = keys.All(remap.Keep.Contains),
                Margin = new Thickness(0, 0, 18, 6),
                MinWidth = 120,
            };
            box.Checked += (_, _) =>
            {
                foreach (var key in keys) if (!remap.Keep.Contains(key)) remap.Keep.Add(key);
                Changed();
            };
            box.Unchecked += (_, _) =>
            {
                remap.Keep.RemoveAll(keys.Contains);
                Changed();
            };
            boxes.Children.Add(box);
        }
        var header = new StackPanel();
        header.Children.Add(Ui.Text("Keep these as Alt shortcuts", 14));
        var description = Ui.Text($"While you hold {KeyChanges.KeyNames.Name(remap.From)}, pressing one of these keys makes it a real Alt again until you let go. " +
                                  "Everything else keeps working as " + KeyChanges.KeyNames.Name(remap.To) + ".", 12, secondary: true);
        description.Margin = new Thickness(0, 2, 0, 0);
        header.Children.Add(description);
        header.Children.Add(boxes);
        return new CardControl
        {
            Icon = Ui.Icon(SymbolRegular.KeyboardShift24, 20),
            Header = header,
            Margin = new Thickness(28, -2, 0, 8),
        };
    }

    /// <summary>A box showing a key; click it, then press a key on the keyboard to choose another.</summary>
    private Button KeyPicker(int value, bool allowOff, Action<int> changed)
    {
        var button = new Button { MinWidth = 150, Appearance = ControlAppearance.Secondary, Icon = Ui.Icon(SymbolRegular.Keyboard24) };
        bool waiting = false;
        void Show() => button.Content = waiting ? "Press a key…" : value == 0 && !allowOff ? "Choose a key" : KeyChanges.KeyNames.Name(value);
        Show();
        button.ToolTip = allowOff
            ? "Click, then press the key it should act as. Right-click to turn the key off."
            : "Click, then press the key to change.";
        button.Click += (_, _) =>
        {
            if (waiting)
            {
                waiting = false;
                _host.KeyChanges.CancelCapture();
                Show();
                return;
            }
            waiting = true;
            Show();
            _host.KeyChanges.CaptureNextKey(vk =>
            {
                waiting = false;
                value = vk;
                Show();
                changed(vk);
            });
        };
        button.LostKeyboardFocus += (_, _) =>
        {
            if (!waiting) return;
            waiting = false;
            _host.KeyChanges.CancelCapture();
            Show();
        };
        var menu = new ContextMenu();
        var pick = new System.Windows.Controls.MenuItem { Header = "Choose from a list" };
        foreach (var (key, name) in KeyChanges.KeyNames.All)
        {
            var item = new System.Windows.Controls.MenuItem { Header = name };
            item.Click += (_, _) => { value = key; Show(); changed(key); };
            pick.Items.Add(item);
        }
        menu.Items.Add(pick);
        if (allowOff)
        {
            var off = new System.Windows.Controls.MenuItem { Header = "Turn the key off (does nothing)" };
            off.Click += (_, _) => { value = 0; Show(); changed(0); };
            menu.Items.Add(off);
        }
        button.ContextMenu = menu;
        return button;
    }

    private UIElement BuildHistory()
    {
        var t = S.Translator;
        var p = new StackPanel();
        p.Children.Add(Ui.Card(SymbolRegular.History24, "Keep a history of translations", "Saved only on this PC.",
            Ui.Toggle(t.KeepHistory, v => { t.KeepHistory = v; Changed(); })));

        var search = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Search history", Icon = Ui.Icon(SymbolRegular.Search24), Margin = new Thickness(0, 12, 0, 8) };
        var list = new StackPanel();

        void Fill()
        {
            list.Children.Clear();
            var entries = _host.History.Search(search.Text).Take(200).ToList();
            if (entries.Count == 0)
            {
                var empty = Ui.Text("Nothing here yet.", 13, secondary: true);
                empty.Margin = new Thickness(4, 8, 0, 0);
                list.Children.Add(empty);
            }
            foreach (var entry in entries)
            {
                var content = new StackPanel();
                var src = Ui.Text(Shorten(entry.Source), 13);
                src.FlowDirection = Languages.LooksRightToLeft(entry.Source) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                var dst = Ui.Text(Shorten(entry.Translation), 13, secondary: true);
                dst.FlowDirection = Languages.IsRightToLeft(entry.TargetLanguage) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                var meta = Ui.Text($"{Languages.NameOf(entry.SourceLanguage)} → {Languages.NameOf(entry.TargetLanguage)}  ·  {entry.Time:g}", 11, secondary: true);
                meta.Margin = new Thickness(0, 4, 0, 0);
                content.Children.Add(src);
                content.Children.Add(dst);
                content.Children.Add(meta);
                var card = new CardAction { Content = content, Margin = new Thickness(0, 0, 0, 4), IsChevronVisible = true };
                card.Click += (_, _) => _host.ShowHistoryEntry(entry);
                list.Children.Add(card);
            }
        }

        search.TextChanged += (_, _) => Fill();
        Fill();

        var clear = Ui.Button("Clear history", () =>
        {
            if (System.Windows.MessageBox.Show(this, "Delete all saved translations?", "Adawat", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _host.History.Clear();
                Fill();
            }
        }, icon: SymbolRegular.Delete24);
        clear.HorizontalAlignment = HorizontalAlignment.Left;
        clear.Margin = new Thickness(0, 4, 0, 0);

        p.Children.Add(clear);
        p.Children.Add(search);
        p.Children.Add(list);
        return p;
    }

    private UIElement BuildAbout()
    {
        var p = new StackPanel();
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new Image { Source = AppIcon.ImageSource, Width = 64, Height = 64 });
        var names = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Ui.Text("Adawat  أدوات", 22, weight: FontWeights.SemiBold));
        names.Children.Add(Ui.Text($"Version {AppPaths.Version}  ·  A small toolkit for Windows", 13, secondary: true));
        header.Children.Add(names);
        p.Children.Add(header);

        p.Children.Add(Ui.Card(SymbolRegular.ArrowDownload24, "Updates", "Download the newest setup file from the releases page.",
            Ui.Button("Open releases page", () => OpenUri(AppPaths.ReleasesUrl))));
        p.Children.Add(Ui.Card(SymbolRegular.Folder24, "Settings and log files", AppPaths.DataFolder,
            Ui.Button("Open folder", () => OpenUri(AppPaths.DataFolder))));
        p.Children.Add(Ui.Card(SymbolRegular.Info24, "Credits",
            "Translations and voice by Google Translate. Text recognition by Windows. Interface built with WPF UI.", null));
        return p;
    }

    // ---- Building blocks ------------------------------------------------------------------

    /// <summary>
    /// The translator's colors: the app's accent, single colors and two-color pairs (split swatches),
    /// in two rows; the chosen one has a ring around it.
    /// </summary>
    private UIElement TranslatorColorPicker() => ColorChoicePicker(() => S.Translator.Colors, v => S.Translator.Colors = v);

    private UIElement ColorChoicePicker(Func<TranslatorColors> get, Action<TranslatorColors> set)
    {
        var rows = new StackPanel();
        var singles = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var pairs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
        rows.Children.Add(singles);
        rows.Children.Add(pairs);
        var rings = new List<(TranslatorColors Choice, Border Ring)>();

        void Refresh()
        {
            var ringColor = new SolidColorBrush(ThemeHelper.AccentColor);
            foreach (var (choice, ring) in rings) ring.BorderBrush = choice == get() ? ringColor : Brushes.Transparent;
        }

        void Add(Panel row, TranslatorColors choice, Brush fill, string name)
        {
            var dot = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = fill };
            var ring = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                BorderThickness = new Thickness(2),
                Child = dot,
                Margin = new Thickness(2, 0, 2, 0),
                Cursor = Cursors.Hand,
                ToolTip = name,
                Background = Brushes.Transparent,
            };
            // React to the press: the settings row captures the mouse, so a MouseUp would never arrive.
            ring.PreviewMouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                set(choice);
                Refresh();
                Changed();
            };
            ring.MouseEnter += (_, _) => dot.Opacity = 0.8;
            ring.MouseLeave += (_, _) => dot.Opacity = 1;
            rings.Add((choice, ring));
            row.Children.Add(ring);
        }

        var (appA, _) = ThemeHelper.TranslatorPair(TranslatorColors.AppAccent);
        var app = new LinearGradientBrush(new GradientStopCollection
        {
            new(appA, 0), new(appA, 0.5), new(Colors.White, 0.5), new(Colors.White, 1),
        }, new Point(0, 0), new Point(1, 1));
        Add(singles, TranslatorColors.AppAccent, app, "App color (the accent color in General)");
        foreach (var (choice, (a, b, name)) in ThemeHelper.TranslatorPalettes)
        {
            bool two = a != b;
            Brush fill = two ? new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 1)) : new SolidColorBrush(a);
            Add(two ? pairs : singles, choice, fill, name);
        }
        Refresh();
        return rows;
    }

    /// <summary>A row of round color swatches; the chosen one has a ring around it.</summary>
    private UIElement AccentPicker()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var rings = new List<(AccentChoice Choice, Border Ring)>();

        void Refresh()
        {
            foreach (var (choice, ring) in rings)
                ring.BorderBrush = choice == S.Accent ? new SolidColorBrush(ThemeHelper.AccentColor) : Brushes.Transparent;
        }

        void Add(AccentChoice choice, Brush fill, string name)
        {
            var dot = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = fill };
            var ring = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                BorderThickness = new Thickness(2),
                Child = dot,
                Margin = new Thickness(2, 0, 2, 0),
                Cursor = Cursors.Hand,
                ToolTip = name,
                Background = Brushes.Transparent,
            };
            // The settings row is itself a button that captures the mouse on press, so a
            // MouseUp here would never arrive: react to the press, before the row sees it.
            ring.PreviewMouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                S.Accent = choice;
                ThemeHelper.Set(S.Theme, S.Accent);
                Refresh();
                Changed();
            };
            ring.MouseEnter += (_, _) => dot.Opacity = 0.8;
            ring.MouseLeave += (_, _) => dot.Opacity = 1;
            rings.Add((choice, ring));
            row.Children.Add(ring);
        }

        foreach (var (choice, color) in ThemeHelper.AccentColors) Add(choice, new SolidColorBrush(color), choice.ToString());
        // "Same as Windows": a split swatch, since the color depends on the Windows settings.
        var windows = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0x00, 0x67, 0xC0), 0), new(Color.FromRgb(0x00, 0x67, 0xC0), 0.5),
            new(Color.FromRgb(0xC2, 0xC9, 0xD3), 0.5), new(Color.FromRgb(0xC2, 0xC9, 0xD3), 1),
        }, new Point(0, 0), new Point(1, 1));
        Add(AccentChoice.Windows, windows, "Same as Windows");
        Refresh();
        return row;
    }

    private CardControl HotkeyCard(SymbolRegular icon, string title, string description, string name, string value, Action<string> changed)
    {
        var box = new HotkeyBox(value, _host.Hotkeys, changed);
        var card = Ui.Card(icon, title, description, box);
        void ShowConflict()
        {
            if (card.Header is StackPanel header)
            {
                const string tag = "conflict";
                foreach (var old in header.Children.OfType<TextBlock>().Where(b => (b.Tag as string) == tag).ToList()) header.Children.Remove(old);
                if (_host.Hotkeys.Conflicts.Contains(name))
                {
                    var warning = Ui.Text("This shortcut is already used by another app. Pick a different one.", 12);
                    warning.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x7A, 0x00));
                    warning.Tag = tag;
                    warning.Margin = new Thickness(0, 4, 0, 0);
                    header.Children.Add(warning);
                }
            }
        }
        ShowConflict();
        _host.Hotkeys.ConflictsChanged += ShowConflict;
        Closed += (_, _) => _host.Hotkeys.ConflictsChanged -= ShowConflict;
        return card;
    }

    private static ComboBox EnumCombo<T>(T value, IReadOnlyDictionary<T, string> labels, Action<T> changed) where T : notnull
    {
        var combo = new ComboBox { MinWidth = 220 };
        foreach (var (key, label) in labels) combo.Items.Add(new ComboBoxItem { Content = label, Tag = key });
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, value)) ?? combo.Items[0];
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: T v }) changed(v);
        };
        return combo;
    }

    private ComboBox LanguageCombo(string value, bool includeAuto, bool includeNone, Action<string> changed)
    {
        var combo = new ComboBox { MinWidth = 220, MaxDropDownHeight = 420 };
        if (includeNone) combo.Items.Add(new ComboBoxItem { Content = "Off", Tag = "" });
        if (includeAuto) combo.Items.Add(new ComboBoxItem { Content = "Detect language", Tag = Languages.Auto });
        foreach (var l in Languages.Ordered(S.Translator.FavoriteLanguages)) combo.Items.Add(new ComboBoxItem { Content = l.Name, Tag = l.Code });
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, value, StringComparison.OrdinalIgnoreCase))
                             ?? combo.Items[0];
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string v }) changed(v);
        };
        return combo;
    }

    private static UIElement SliderRow(double min, double max, double value, Func<double, string> format, Action<double> changed)
    {
        var label = Ui.Text(format(value), 13, secondary: true);
        label.Width = 52;
        label.TextAlignment = TextAlignment.Right;
        label.VerticalAlignment = VerticalAlignment.Center;
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 220, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) => label.Text = format(e.NewValue);
        // Save once the user lets go, not on every pixel of the drag.
        slider.PreviewMouseUp += (_, _) => changed(slider.Value);
        slider.KeyUp += (_, _) => changed(slider.Value);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(slider);
        row.Children.Add(label);
        return row;
    }

    private static UIElement CommaListBox(List<string> values, Action<List<string>> changed)
    {
        var box = new Wpf.Ui.Controls.TextBox { Text = string.Join(", ", values), MinWidth = 220 };
        box.LostFocus += (_, _) =>
            changed(box.Text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList());
        return box;
    }

    private CardControl AppListCard(string title, string description, List<string> apps, Action<List<string>> changed)
    {
        var box = new TextBox
        {
            Text = string.Join(Environment.NewLine, apps),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 90,
            MaxHeight = 160,
            Width = 240,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        box.LostFocus += (_, _) =>
            changed(box.Text.Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList());
        return Ui.Card(SymbolRegular.AppsList24, title, description, box);
    }

    private static string Shorten(string text)
    {
        text = text.Replace('\n', ' ').Replace('\r', ' ');
        return text.Length > 160 ? text[..160] + "…" : text;
    }

    private static void OpenUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open {uri}", ex);
        }
    }
}
