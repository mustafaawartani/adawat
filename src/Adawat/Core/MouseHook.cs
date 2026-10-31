using System;
using System.Runtime.InteropServices;
using System.Threading;
using Adawat.Interop;

namespace Adawat.Core;

public enum MouseEventKind { LeftDown, LeftUp, OtherButtonDown, Wheel }

/// <summary>
/// A mouse event. <see cref="IsTextCursor"/> is only filled in for left button presses and releases;
/// on a release it is also true when the text cursor (I-beam) showed at any moment of the drag.
/// </summary>
public readonly record struct MouseHookEvent(MouseEventKind Kind, int X, int Y, long TimestampMs, bool IsTextCursor);

/// <summary>
/// A global low-level mouse hook running on its own thread, so a busy UI never makes the
/// mouse lag. It only observes; it never blocks input. Events are raised on the hook thread.
/// </summary>
public sealed class MouseHook : IDisposable
{
    private readonly Native.LowLevelMouseProc _proc;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private long _lastEventMs = Environment.TickCount64;
    private long _startedMs;
    private bool _leftDown;
    private bool _dragSawText;
    private long _lastDragSampleMs;
    private Native.POINT _lastCheckCursor;
    private long _lastRestartMs;

    public event Action<MouseHookEvent>? MouseEvent;

    /// <summary>
    /// Asked on the hook thread for every real wheel event; returning true swallows it so no app
    /// sees it. Must be very fast.
    /// </summary>
    public Func<bool>? BlockWheel { get; set; }

    public bool IsRunning => _thread != null;

    public MouseHook()
    {
        _proc = HookProc; // keep the delegate alive
    }

    public void Start()
    {
        if (_thread != null) return;
        _startedMs = _lastEventMs = Environment.TickCount64;
        var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = Native.GetCurrentThreadId();
            _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc, Native.GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) Log.Error($"Mouse hook failed: {Marshal.GetLastWin32Error()}");
            ready.Set();
            while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }
            if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        })
        {
            IsBackground = true,
            Name = "Adawat mouse hook",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
        ready.Wait(2000);
    }

    public void Stop()
    {
        if (_thread == null) return;
        Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
    }

    /// <summary>
    /// Windows silently removes a low-level hook that once answered too slowly (for example while
    /// the PC was very busy or waking from sleep); after that, nothing reaches Adawat any more and
    /// the translate button never shows. Call this every few seconds: if the pointer moved by hand
    /// but the hook heard nothing for a while, the hook is installed again.
    /// </summary>
    public void CheckAlive()
    {
        if (_thread == null) return;
        var cursor = WindowInfo.CursorPosition();
        bool moved = cursor.X != _lastCheckCursor.X || cursor.Y != _lastCheckCursor.Y;
        _lastCheckCursor = cursor;
        long now = Environment.TickCount64;
        long silentFor = now - Interlocked.Read(ref _lastEventMs);
        if (!moved || silentFor < 4000 || now - _startedMs < 4000 || now - _lastRestartMs < 30000) return;
        // The pointer can also move without the hook (the screen zoom moves it): only real input counts.
        if (now - LastInputMs() > 2000) return;
        _lastRestartMs = now;
        Log.Info($"The mouse hook was silent for {silentFor / 1000} s while the mouse moved; installing it again");
        Restart();
    }

    /// <summary>Installs the hook again (after the PC wakes up or the session is unlocked, Windows may have dropped it).</summary>
    public void Restart()
    {
        if (_thread == null) return;
        Stop();
        Start();
    }

    private static long LastInputMs()
    {
        var info = new Native.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<Native.LASTINPUTINFO>() };
        if (!Native.GetLastInputInfo(ref info)) return Environment.TickCount64;
        // dwTime is a 32-bit tick count; compare in the same 32-bit space.
        uint elapsed = unchecked((uint)Environment.TickCount - info.dwTime);
        return Environment.TickCount64 - elapsed;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
                long now = Environment.TickCount64;
                Interlocked.Exchange(ref _lastEventMs, now);
                if ((int)wParam == Native.WM_MOUSEMOVE)
                {
                    // While dragging, look at the pointer now and then: a selection that starts and
                    // ends beside the text (in the margin) still passes over it on the way.
                    if (_leftDown && !_dragSawText && now - _lastDragSampleMs >= 30)
                    {
                        _lastDragSampleMs = now;
                        _dragSawText = WindowInfo.IsTextBeamCursor();
                    }
                }
                else if (data.dwExtraInfo != InputSender.Signature)
                {
                    MouseEventKind? kind = (int)wParam switch
                    {
                        Native.WM_LBUTTONDOWN => MouseEventKind.LeftDown,
                        Native.WM_LBUTTONUP => MouseEventKind.LeftUp,
                        Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN => MouseEventKind.OtherButtonDown,
                        Native.WM_MOUSEWHEEL or Native.WM_MOUSEHWHEEL => MouseEventKind.Wheel,
                        _ => null,
                    };
                    bool text = false;
                    if (kind == MouseEventKind.LeftDown)
                    {
                        text = WindowInfo.IsLikelyTextCursor();
                        _leftDown = true;
                        _dragSawText = text;
                        _lastDragSampleMs = now;
                    }
                    else if (kind == MouseEventKind.LeftUp)
                    {
                        text = _dragSawText || WindowInfo.IsLikelyTextCursor();
                        _leftDown = false;
                    }
                    if (kind != null) MouseEvent?.Invoke(new MouseHookEvent(kind.Value, data.pt.X, data.pt.Y, now, text));
                    if (kind == MouseEventKind.Wheel && BlockWheel?.Invoke() == true)
                    {
                        return new IntPtr(1);
                    }
                }
            }
            catch
            {
                // Never let an exception escape into the hook chain.
            }
        }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
