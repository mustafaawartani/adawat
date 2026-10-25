using System;
using System.Runtime.InteropServices;
using System.Threading;
using Adawat.Interop;

namespace Adawat.Core;

public enum MouseEventKind { LeftDown, LeftUp, OtherButtonDown, Wheel }

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

    public event Action<MouseHookEvent>? MouseEvent;

    public bool IsRunning => _thread != null;

    public MouseHook()
    {
        _proc = HookProc; // keep the delegate alive
    }

    public void Start()
    {
        if (_thread != null) return;
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

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
                if (data.dwExtraInfo != InputSender.Signature)
                {
                    MouseEventKind? kind = (int)wParam switch
                    {
                        Native.WM_LBUTTONDOWN => MouseEventKind.LeftDown,
                        Native.WM_LBUTTONUP => MouseEventKind.LeftUp,
                        Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN => MouseEventKind.OtherButtonDown,
                        Native.WM_MOUSEWHEEL or Native.WM_MOUSEHWHEEL => MouseEventKind.Wheel,
                        _ => null,
                    };
                    if (kind != null && MouseEvent != null)
                    {
                        bool text = kind == MouseEventKind.LeftDown && WindowInfo.IsLikelyTextCursor();
                        MouseEvent(new MouseHookEvent(kind.Value, data.pt.X, data.pt.Y, Environment.TickCount64, text));
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
