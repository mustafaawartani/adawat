using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Adawat.Core;
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
        WindowBackdropType = WindowBackdropType.Mica;
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
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(body, 1);
        root.Children.Add(titleBar);
        root.Children.Add(body);
        Content = root;

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
        tips.Children.Add(Ui.Text("Adawat runs quietly in the system tray, next to the clock. Click its icon to open the translator.", 13));
        foreach (var line in new[]
                 {
                     $"{S.Translator.TranslateHotkey}  —  translate the selected text in any app",
                     $"{S.Ocr.Hotkey}  —  read text from any part of the screen (pictures, videos, scanned PDFs)",
                     $"{S.Translator.OpenBoxHotkey}  —  open the translator box",
                     "Select text with the mouse  —  a small translate button appears next to it",
                     "Two-finger swipe left/right  —  back/forward;  pinch  —  zoom",
                     $"Rest the pointer on a picture  —  see it big;  {S.ImageZoom.SaveHotkey} saves it to Downloads, {S.ImageZoom.CopyHotkey} copies it",
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
            p.Children.Add(Ui.Card(SymbolRegular.Gauge24, "Zoom speed", "How fast the screen zooms as your fingers move.",
                SliderRow(1, 100, g.PinchSensitivity, v => $"{v:0}", v => { g.PinchSensitivity = (int)Math.Round(v); Changed(); })));
            p.Children.Add(Ui.Card(SymbolRegular.ZoomIn24, "Maximum zoom", null,
                SliderRow(2, 10, g.MaxScreenZoom, v => $"{v:0}x", v => { g.MaxScreenZoom = Math.Round(v); Changed(); })));
            p.Children.Add(Ui.Card(SymbolRegular.Eye24, "Picture",
                "Sharp keeps text and edges crisp. Smooth softens the enlarged pixels, which looks gentler on photos but a little blurry.",
                EnumCombo(g.SmoothZoomEdges, new Dictionary<bool, string> { [false] = "Sharp", [true] = "Smooth" },
                    v => { g.SmoothZoomEdges = v; Changed(); })));
            p.Children.Add(Ui.Card(SymbolRegular.ZoomFit24, "Settle on whole zoom levels",
                "When you lift your fingers close to 2x, 3x…, the zoom settles exactly there, where the picture is sharpest.",
                Ui.Toggle(g.SnapZoomLevels, v => { g.SnapZoomLevels = v; Changed(); })));
            p.Children.Add(Ui.Card(SymbolRegular.ArrowMove24, "Moving around while zoomed",
                "While zoomed, slide two fingers in any direction to move the zoomed view, like in Chrome. " +
                "The app underneath does not scroll and back/forward swipes pause until you zoom out.",
                SliderRow(1, 100, g.PanSpeed, v => $"{v:0}", v => { g.PanSpeed = (int)Math.Round(v); Changed(); })));
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
            SliderRow(24, 200, z.MinSize, v => $"{v:0} px", v => { z.MinSize = (int)Math.Round(v); Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.ScreenSearch24, "Enlarge pictures from the screen",
            "Picture files and pictures that link to their full size open at full quality. For other pictures (most web pages) " +
            "Adawat can only enlarge what is on screen, which is less sharp. Turn off to open only full-quality pictures.",
            Ui.Toggle(z.EnlargeScreenPictures, v => { z.EnlargeScreenPictures = v; Changed(); })));
        p.Children.Add(Ui.Card(SymbolRegular.Games24, "Pause in full-screen apps", "Games, videos and presentations.",
            Ui.Toggle(z.DisableInFullscreen, v => { z.DisableInFullscreen = v; Changed(); })));
        p.Children.Add(AppListCard("Apps where picture zoom is off", "One program per line, e.g. photoshop.exe", z.DisabledApps, v => { z.DisabledApps = v; Changed(); }));
        return p;
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
            ring.MouseLeftButtonUp += (_, _) =>
            {
                S.Accent = choice;
                ThemeHelper.Set(S.Theme, S.Accent);
                Refresh();
                Changed();
            };
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
        label.Width = 44;
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
