using System;
using System.Runtime.InteropServices;
using System.Threading;
using Adawat.Core;
using Adawat.Interop;

namespace Adawat.KeyChanges;

/// <summary>
/// A global low-level keyboard hook on its own thread, which turns keys into other keys
/// (see <see cref="KeyRemapper"/>) and can catch the next key press for the settings window.
/// The hook only swallows the keys it changes, and answers right away so typing never lags.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const uint LLKHF_UP = 0x80;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    private readonly LowLevelKeyboardProc _proc;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private Action<int>? _capture;      // called (on the hook thread) with the next key pressed
    private int _swallowRelease;        // the captured key: its release is swallowed too

    public KeyRemapper Remapper { get; } = new();

    public bool IsRunning => _thread != null;

    public KeyboardHook()
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
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) Log.Error($"Keyboard hook failed: {Marshal.GetLastWin32Error()}");
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
            Name = "Adawat keyboard hook",
            Priority = ThreadPriority.Highest,
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
        ReleaseHeldKeys();
    }

    /// <summary>Installs the hook again (Windows can drop hooks while the PC sleeps or is locked).</summary>
    public void Restart()
    {
        if (_thread == null) return;
        Stop();
        Start();
    }

    /// <summary>The next key pressed is swallowed and passed to <paramref name="onKey"/> (on the hook thread).</summary>
    public void CaptureNext(Action<int>? onKey) => Volatile.Write(ref _capture, onKey);

    /// <summary>Lets go of keys Adawat is holding down for a changed key (when the rules change or the hook stops).</summary>
    public void ReleaseHeldKeys()
    {
        foreach (var key in Remapper.ReleaseAll()) InputSender.SendKey((ushort)key, up: true);
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                bool up = (data.flags & LLKHF_UP) != 0;
                bool ours = data.dwExtraInfo == InputSender.Signature;
                int vk = (int)data.vkCode;

                if (!ours)
                {
                    var capture = Volatile.Read(ref _capture);
                    if (capture != null && !up)
                    {
                        Volatile.Write(ref _capture, null);
                        _swallowRelease = vk;
                        capture(vk);
                        return new IntPtr(1);
                    }
                    if (up && vk == _swallowRelease)
                    {
                        _swallowRelease = 0;
                        return new IntPtr(1);
                    }
                }

                var decision = Remapper.Process(vk, (int)data.scanCode, up, ours);
                if (decision.Swallow)
                {
                    if (decision.Send.Count > 0) InputSender.SendKeys(decision.Send);
                    return new IntPtr(1);
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
