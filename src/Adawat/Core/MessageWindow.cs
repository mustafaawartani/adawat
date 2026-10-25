using System;
using System.Windows.Interop;
using Adawat.Interop;

namespace Adawat.Core;

/// <summary>
/// An invisible window on the UI thread that receives WM_HOTKEY and WM_INPUT (touchpad).
/// It is a hidden top-level window rather than a message-only one, because raw input
/// with RIDEV_INPUTSINK is only reliably delivered to top-level windows.
/// </summary>
public sealed class MessageWindow : IDisposable
{
    public delegate void MessageHandler(int msg, IntPtr wParam, IntPtr lParam, ref bool handled);

    private readonly HwndSource _source;

    public event MessageHandler? Message;

    public IntPtr Handle => _source.Handle;

    public MessageWindow()
    {
        var parameters = new HwndSourceParameters("AdawatMessages")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, never shown
            ExtendedWindowStyle = (int)Native.WS_EX_TOOLWINDOW,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        try
        {
            Message?.Invoke(msg, wParam, lParam, ref handled);
        }
        catch (Exception ex)
        {
            Log.Error($"Message 0x{msg:X} handler failed", ex);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
