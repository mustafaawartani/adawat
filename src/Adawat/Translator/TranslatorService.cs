using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Adawat.Core;
using Adawat.Interop;

namespace Adawat.Translator;

/// <summary>
/// The translator tool: shortcuts, the icon that appears after selecting text, and the popup.
/// </summary>
public sealed class TranslatorService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;
    private readonly TextGrabber _grabber;
    private readonly Func<TranslatorWindow> _window;
    private SelectionIconWindow? _icon;
    private bool _busy;

    // Selection tracking (fed by the mouse hook)
    private int _downX, _downY;
    private long _downTime;
    private bool _downOnText;
    private long _lastClickTime;
    private int _lastClickX, _lastClickY;
    private int _clickCount;
    private IntPtr _iconOwner;
    private DispatcherTimer? _selectionWatch;
    private long _iconShownAt;

    public TranslatorService(AppSettings settings, Func<TranslatorWindow> window)
    {
        _settings = settings;
        _window = window;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _grabber = new TextGrabber(settings.Translator);
    }

    private TranslatorSettings T => _settings.Translator;

    public bool WantsMouseHook => T.Enabled && T.OnSelection != SelectionBehavior.DoNothing;

    /// <summary>Shortcut: translate whatever is selected in the active app (or open an empty box).</summary>
    public void TranslateSelection()
    {
        if (!T.Enabled) return;
        // Alt is still held: tap a dummy key so releasing it does not open the app's menu bar.
        InputSender.MaskMenuKey();
        _ = CaptureAndShowAsync(WindowInfo.CursorPosition(), allowEmpty: true);
    }

    /// <summary>Shortcut / tray: open the translator with an empty box.</summary>
    public void OpenBox()
    {
        InputSender.MaskMenuKey();
        _window().ShowFor(null, PopupMode.Manual, WindowInfo.CursorPosition());
    }

    public void TranslateText(string text, PopupMode mode, Native.POINT anchor, bool translateNow = true, string? error = null) =>
        _window().ShowFor(text, mode, anchor, translateNow, error);

    private async Task CaptureAndShowAsync(Native.POINT anchor, bool allowEmpty)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var foreground = Native.GetForegroundWindow();
            if (WindowInfo.IsOwnWindow(foreground))
            {
                // Our own popup is in front: just bring the box forward.
                if (allowEmpty) _window().ShowFor(null, PopupMode.Manual, anchor);
                return;
            }

            var process = WindowInfo.ProcessName(foreground);
            if (WindowInfo.ListContains(T.DisabledApps, process)) return;

            var text = await _grabber.GrabAsync(process);
            if (string.IsNullOrWhiteSpace(text))
            {
                if (allowEmpty) _window().ShowFor(null, PopupMode.Manual, anchor);
                return;
            }
            _window().ShowFor(text, PopupMode.Selection, anchor);
        }
        catch (Exception ex)
        {
            Log.Error("Could not translate the selection", ex);
        }
        finally
        {
            _busy = false;
        }
    }

    // ---- Selection detection -------------------------------------------------------------

    /// <summary>Called from the mouse hook thread.</summary>
    public void OnMouse(MouseHookEvent e)
    {
        switch (e.Kind)
        {
            case MouseEventKind.LeftDown:
                _downX = e.X;
                _downY = e.Y;
                _downTime = e.TimestampMs;
                _downOnText = e.IsTextCursor;
                _dispatcher.BeginInvoke(() =>
                {
                    if (_icon != null && _icon.IsShown && !_icon.Contains(e.X, e.Y)) _icon.HideIcon();
                });
                break;

            case MouseEventKind.LeftUp:
                HandleLeftUp(e);
                break;

            case MouseEventKind.OtherButtonDown:
            case MouseEventKind.Wheel:
                // Lifting fingers off a touchpad right after selecting can send a tiny scroll; ignore that.
                if (e.Kind == MouseEventKind.Wheel && e.TimestampMs - Interlocked.Read(ref _iconShownAt) < 600) break;
                _dispatcher.BeginInvoke(() =>
                {
                    if (_icon != null && _icon.IsShown && !_icon.Contains(e.X, e.Y)) _icon.HideIcon();
                });
                break;
        }
    }

    private void HandleLeftUp(MouseHookEvent e)
    {
        long sinceLast = e.TimestampMs - _lastClickTime;
        bool nearLast = Math.Abs(e.X - _lastClickX) < 6 && Math.Abs(e.Y - _lastClickY) < 6;
        _clickCount = sinceLast <= Native.GetDoubleClickTime() && nearLast ? _clickCount + 1 : 1;
        _lastClickTime = e.TimestampMs;
        _lastClickX = e.X;
        _lastClickY = e.Y;

        int distance = Math.Abs(e.X - _downX) + Math.Abs(e.Y - _downY);
        bool dragged = distance >= 8 && e.TimestampMs - _downTime >= 100;
        bool doubleClick = _clickCount >= 2;
        if (!dragged && !doubleClick)
        {
            if (distance >= 8) Explain("the drag was too quick to be a selection");
            return;
        }
        // A selection often starts just beside the text (arrow pointer) and ends on it, or the other way round.
        if (T.OnlyOverText && !_downOnText && !e.IsTextCursor)
        {
            Explain("the pointer was not over text when pressing or releasing (see \"Only when selecting text\")");
            return;
        }
        if (!ModifierSatisfied())
        {
            Explain($"{T.SelectionModifier} was not held");
            return;
        }

        var point = new Native.POINT(e.X, e.Y);
        _dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
        {
            if (!WantsMouseHook) return;
            var foreground = Native.GetForegroundWindow();
            if (WindowInfo.IsOwnWindow(foreground)) return;
            if (WindowInfo.ListContains(T.DisabledApps, WindowInfo.ProcessName(foreground)))
            {
                Explain($"{WindowInfo.ProcessName(foreground)} is in the list of apps where the translator is off");
                return;
            }

            await Task.Delay(80); // let the app finish updating its selection
            if (T.OnSelection == SelectionBehavior.TranslateInstantly)
            {
                await CaptureAndShowAsync(point, allowEmpty: false);
            }
            else
            {
                _iconOwner = foreground;
                Interlocked.Exchange(ref _iconShownAt, Environment.TickCount64);
                Icon.ShowAt(point, T.KeepIconWhileSelected ? null : T.IconSeconds);
                if (T.KeepIconWhileSelected) WatchSelection();
            }
        });
    }

    private SelectionIconWindow Icon
    {
        get
        {
            if (_icon != null) return _icon;
            _icon = new SelectionIconWindow();
            _icon.Clicked += p =>
            {
                // Make sure the app that owns the selection is still in front before copying.
                if (_iconOwner != IntPtr.Zero && Native.GetForegroundWindow() != _iconOwner) Native.SetForegroundWindow(_iconOwner);
                _ = CaptureAndShowAsync(p, allowEmpty: false);
            };
            return _icon;
        }
    }

    /// <summary>
    /// While the button waits next to a selection, watch for what clears a selection that the mouse
    /// hook cannot see: typing (Ctrl shortcuts such as Ctrl+C keep it) and switching to another app.
    /// Clicks elsewhere and scrolling are handled in <see cref="OnMouse"/>.
    /// </summary>
    private void WatchSelection()
    {
        if (_selectionWatch == null)
        {
            _selectionWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _selectionWatch.Tick += (_, _) =>
            {
                if (_icon == null || !_icon.IsShown)
                {
                    _selectionWatch.Stop();
                    return;
                }
                var foreground = Native.GetForegroundWindow();
                bool otherApp = foreground != _iconOwner && !WindowInfo.IsOwnWindow(foreground);
                if (otherApp || !Native.IsWindow(_iconOwner) || TypingKeyDown())
                {
                    _selectionWatch.Stop();
                    _icon.HideIcon();
                }
            };
        }
        _selectionWatch.Stop();
        _selectionWatch.Start();
    }

    /// <summary>Keys that type or move the caret, which replace or clear a selection.</summary>
    private static readonly int[] TypingKeys = BuildTypingKeys();

    private static int[] BuildTypingKeys()
    {
        var keys = new System.Collections.Generic.List<int> { 0x08, 0x09, 0x0D, 0x2E }; // Backspace, Tab, Enter, Delete
        for (int vk = 0x20; vk <= 0x28; vk++) keys.Add(vk);  // Space, Page Up/Down, End, Home, arrows
        for (int vk = 0x30; vk <= 0x39; vk++) keys.Add(vk);  // 0-9
        for (int vk = 0x41; vk <= 0x5A; vk++) keys.Add(vk);  // A-Z
        for (int vk = 0x60; vk <= 0x6F; vk++) keys.Add(vk);  // number pad
        for (int vk = 0xBA; vk <= 0xC0; vk++) keys.Add(vk);  // ; = , - . / `
        for (int vk = 0xDB; vk <= 0xDF; vk++) keys.Add(vk);  // [ \ ] '
        keys.Add(0xE2);                                      // < > on some keyboards
        return keys.ToArray();
    }

    /// <summary>
    /// Only real typing keys count: language-switch and input-method keys can read as held down
    /// for a long time, which used to hide the button the moment it appeared.
    /// </summary>
    private static bool TypingKeyDown()
    {
        if (InputSender.IsDown(InputSender.VK_CONTROL)) return false; // Ctrl+C, Ctrl+A… keep the selection
        foreach (var vk in TypingKeys)
        {
            if ((Native.GetAsyncKeyState(vk) & 0x8000) != 0) return true;
        }
        return false;
    }

    private static string _lastExplanation = "";
    private static long _lastExplanationMs;

    /// <summary>Notes in the log why a mouse selection did not show the button (not repeated, at most every few seconds).</summary>
    private static void Explain(string reason)
    {
        long now = Environment.TickCount64;
        if (now - _lastExplanationMs < 2000 || (reason == _lastExplanation && now - _lastExplanationMs < 30000)) return;
        _lastExplanation = reason;
        _lastExplanationMs = now;
        // Often called on the mouse hook thread, which must never wait on a file.
        Task.Run(() => Log.Info("Translate button not shown: " + reason));
    }

    private bool ModifierSatisfied() => T.SelectionModifier switch
    {
        SelectionModifier.Alt => InputSender.IsDown(InputSender.VK_MENU),
        SelectionModifier.Ctrl => InputSender.IsDown(InputSender.VK_CONTROL),
        SelectionModifier.Shift => InputSender.IsDown(InputSender.VK_SHIFT),
        _ => true,
    };

    public void HideIcon() => _icon?.HideIcon();

    public void Dispose()
    {
        _selectionWatch?.Stop();
        _icon?.Close();
    }
}
