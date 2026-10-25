using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Adawat.Core;
using Adawat.Gestures;
using Adawat.Ocr;
using Adawat.Translator;
using Adawat.UI;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace Adawat;

/// <summary>The tray application: owns every tool and applies the settings to them.</summary>
public sealed class App : Application, IAppHost
{
    private readonly string[] _args;
    private SingleInstance _instance;

    private MessageWindow _messages = null!;
    private MouseHook _mouseHook = null!;
    private GoogleTranslator _google = null!;
    private SpeechPlayer _speech = null!;
    private TranslatorService _translator = null!;
    private OcrTool _ocr = null!;
    private GestureService _gestures = null!;
    private TrayIcon _tray = null!;
    private TranslatorWindow? _popup;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer _saveTimer = null!;
    private bool? _touchpadPresent;
    private bool? _appliedStartWithWindows;
    private bool _tornDown;

    public AppSettings Settings { get; private set; } = null!;
    public HotkeyManager Hotkeys { get; private set; } = null!;
    public HistoryStore History { get; private set; } = null!;

    public App(string[] args, SingleInstance instance)
    {
        _args = args;
        _instance = instance;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error("Unexpected error", ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Error("Fatal error", ex.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            Log.Error("Background task error", ex.Exception);
            ex.SetObserved();
        };
        SessionEnding += (_, _) => TearDown();

        Log.Info($"Adawat {AppPaths.Version} starting (administrator: {StartupManager.IsElevated})");
        System.Windows.Forms.Application.EnableVisualStyles();

        Settings = SettingsStore.Load();
        Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Light });
        Resources.MergedDictionaries.Add(new ControlsDictionary());
        ThemeHelper.Initialize(Settings.Theme);

        _messages = new MessageWindow();
        Hotkeys = new HotkeyManager(_messages);
        History = new HistoryStore(Settings.Translator);
        _google = new GoogleTranslator();
        _speech = new SpeechPlayer();
        _translator = new TranslatorService(Settings, () => Popup);
        _ocr = new OcrTool(Settings, _translator);
        _gestures = new GestureService(Settings, _messages);
        _mouseHook = new MouseHook();
        _mouseHook.MouseEvent += OnMouse;
        _tray = new TrayIcon(Settings,
            openBox: () => _translator.OpenBox(),
            translateSelection: () => _translator.TranslateSelection(),
            captureScreen: () => _ocr.CaptureScreen(),
            openSettings: () => OpenSettings(),
            openSettingsPage: page => OpenSettings(page),
            settingsChanged: SettingsChanged,
            exit: ExitApp);

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SettingsStore.Save(Settings);
        };

        ApplySettings();
        _instance.Listen(args => Dispatcher.BeginInvoke(() => HandleArguments(args, atStartup: false)));
        HandleArguments(_args, atStartup: true);

        if (!Settings.FirstRunCompleted)
        {
            Settings.FirstRunCompleted = true;
            SettingsStore.Save(Settings);
            _tray.ShowBalloon("Adawat is running",
                $"Select text and press {Settings.Translator.TranslateHotkey} to translate. Click this icon for the translator box.");
        }
    }

    private TranslatorWindow Popup =>
        _popup ??= new TranslatorWindow(Settings, _google, _speech, History, () => OpenSettings("Translator"), SettingsChanged);

    private void HandleArguments(string[] args, bool atStartup)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--ocr" || args[i] == "--ocr-translate") && i + 1 < args.Length)
            {
                _ = _ocr.ReadFileAsync(args[i + 1], translate: args[i] == "--ocr-translate");
                return;
            }
            if (args[i] == "--show-settings")
            {
                OpenSettings();
                return;
            }
        }
        // Opened by hand (Start menu, or right after installing): show the settings once.
        if (atStartup && !args.Contains("--startup")) OpenSettings();
    }

    private void OnMouse(MouseHookEvent e)
    {
        // Runs on the hook thread.
        if (e.Kind == MouseEventKind.Wheel) _gestures.NotifyNativeWheel();
        if (_translator.WantsMouseHook) _translator.OnMouse(e);
    }

    // ---- Settings ------------------------------------------------------------------------------

    public void SettingsChanged()
    {
        ApplySettings();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void ApplySettings()
    {
        var t = Settings.Translator;
        var o = Settings.Ocr;
        var g = Settings.Gestures;

        Hotkeys.Set("translate", t.Enabled ? t.TranslateHotkey : null, () => Dispatcher.BeginInvoke(_translator.TranslateSelection));
        Hotkeys.Set("openbox", t.Enabled ? t.OpenBoxHotkey : null, () => Dispatcher.BeginInvoke(_translator.OpenBox));
        Hotkeys.Set("ocr", o.Enabled ? o.Hotkey : null, () => Dispatcher.BeginInvoke(_ocr.CaptureScreen));

        bool needHook = _translator.WantsMouseHook || (g.Enabled && g.PinchEnabled && g.SkipNativePinchApps);
        if (needHook) _mouseHook.Start();
        else _mouseHook.Stop();
        if (!_translator.WantsMouseHook) _translator.HideIcon();

        _gestures.Apply();
        StartupManager.ApplyRunKey(Settings);
        if (Settings.RunElevated && _appliedStartWithWindows != null && _appliedStartWithWindows != Settings.StartWithWindows)
        {
            // The scheduled task holds the "at sign-in" part; update it.
            StartupManager.EnableElevatedStartup(Settings.StartWithWindows);
        }
        _appliedStartWithWindows = Settings.StartWithWindows;
        ExplorerMenu.Apply(o.Enabled && o.ImageContextMenu);
    }

    public bool SetRunElevated(bool elevated)
    {
        if (elevated)
        {
            if (!StartupManager.EnableElevatedStartup(Settings.StartWithWindows)) return false;
            Settings.RunElevated = true;
            SettingsStore.Save(Settings);
            StartupManager.ApplyRunKey(Settings);
            if (!StartupManager.IsElevated)
            {
                const string question = "Done. Restart Adawat as administrator now?";
                var answer = _settingsWindow != null
                    ? MessageBox.Show(_settingsWindow, question, "Adawat", MessageBoxButton.YesNo, MessageBoxImage.Question)
                    : MessageBox.Show(question, "Adawat", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Yes) RestartElevated();
            }
            return true;
        }

        if (!StartupManager.DisableElevatedStartup()) return false;
        Settings.RunElevated = false;
        SettingsStore.Save(Settings);
        StartupManager.ApplyRunKey(Settings);
        return true;
    }

    private void RestartElevated()
    {
        TearDown();
        _instance.Dispose();
        if (!StartupManager.StartElevatedNow())
        {
            Log.Info("Could not start the elevated task; restarting normally");
            try { Process.Start(new ProcessStartInfo(AppPaths.ExecutablePath, "--startup") { UseShellExecute = true }); } catch { }
        }
        Shutdown();
    }

    public void ShowHistoryEntry(HistoryEntry entry) => Popup.ShowHistoryEntry(entry);

    public bool TouchpadPresent => _touchpadPresent ??= TouchpadReader.IsTouchpadPresent();

    public async Task<string> TestTranslationAsync()
    {
        try
        {
            var result = await _google.TranslateAsync("Hello, how are you?", "en", Settings.Translator.TargetLanguage);
            return $"Working: \"{result.Translation}\"";
        }
        catch (TranslationException ex)
        {
            return ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error("Translation test failed", ex);
            return "Not working: " + ex.Message;
        }
    }

    private void OpenSettings(string? page = null)
    {
        if (_settingsWindow != null)
        {
            if (page != null) _settingsWindow.ShowPage(page);
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this, page);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            SettingsStore.Save(Settings);
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    // ---- Shutdown ----------------------------------------------------------------------------

    private void ExitApp()
    {
        TearDown();
        Shutdown();
    }

    private void TearDown()
    {
        if (_tornDown) return;
        _tornDown = true;
        try
        {
            _saveTimer.Stop();
            SettingsStore.Save(Settings);
            _mouseHook.Dispose();
            Hotkeys.Dispose();
            _gestures.Dispose();
            _translator.Dispose();
            _speech.Dispose();
            _tray.Dispose();
            _messages.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Error while closing", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TearDown();
        base.OnExit(e);
    }
}
