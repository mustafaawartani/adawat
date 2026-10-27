using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Adawat.Core;
using Adawat.Interop;
using Adawat.UI;

namespace Adawat.Translator;

/// <summary>
/// The small translate button that appears next to selected text. It never takes focus,
/// so the selection stays active in the other app while it is clicked.
/// </summary>
public sealed class SelectionIconWindow : Window
{
    /// <summary>Window size in DIPs; the glass plate inside is 32 DIPs, the rest is room for its shadow.</summary>
    private const double SizeDip = 44;
    private readonly DispatcherTimer _hideTimer;
    private readonly Image _badge;
    private IntPtr _hwnd;
    private double? _seconds;

    public event Action<Native.POINT>? Clicked;

    public SelectionIconWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Width = SizeDip;
        Height = SizeDip;
        Left = -10000;
        Top = -10000;
        Cursor = Cursors.Hand;
        ToolTip = "Translate (Adawat)";

        // A glass badge drawn ahead of time (Assets/translate-button.svg), with its soft shadow
        // inside the picture so nothing is clipped at the window edge.
        _badge = new Image
        {
            Source = AppIcon.TranslateButton,
            Width = SizeDip,
            Height = SizeDip,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
        };
        RenderOptions.SetBitmapScalingMode(_badge, BitmapScalingMode.HighQuality);
        Content = _badge;

        _hideTimer = new DispatcherTimer();
        _hideTimer.Tick += (_, _) => HideIcon();

        MouseEnter += (_, _) =>
        {
            _hideTimer.Stop();
            AnimateScale(1.08, 120);
        };
        MouseLeave += (_, _) =>
        {
            AnimateScale(1, 160);
            if (_seconds != null) RestartTimer(1.5);
        };
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var p = WindowInfo.CursorPosition();
            HideIcon();
            Clicked?.Invoke(p);
        };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
            ex |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
            HwndSource.FromHwnd(_hwnd)?.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == Native.WM_MOUSEACTIVATE)
                {
                    handled = true;
                    return new IntPtr(Native.MA_NOACTIVATE);
                }
                return IntPtr.Zero;
            });
        };
    }

    public bool IsShown => IsVisible && Opacity > 0;

    /// <summary>
    /// Shows the icon near the physical screen point where the selection ended. With no
    /// <paramref name="seconds"/> it stays until <see cref="HideIcon"/> is called.
    /// </summary>
    public void ShowAt(Native.POINT p, double? seconds)
    {
        _seconds = seconds;
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }
        double scale = WindowInfo.ScaleAt(p);
        int size = (int)(SizeDip * scale);
        var work = WindowInfo.WorkAreaAt(p);
        int x = p.X + (int)(4 * scale);
        int y = p.Y - size;
        if (y < work.Top) y = p.Y + (int)(14 * scale);
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - size));
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        var pop = new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new BackEase { Amplitude = 0.35 } };
        ((ScaleTransform)_badge.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        ((ScaleTransform)_badge.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        if (seconds != null) RestartTimer(seconds.Value);
        else _hideTimer.Stop();
    }

    private void AnimateScale(double to, int ms)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new QuadraticEase() };
        ((ScaleTransform)_badge.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        ((ScaleTransform)_badge.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    /// <summary>True if the physical point is on the icon.</summary>
    public bool Contains(int x, int y)
    {
        if (!IsVisible || _hwnd == IntPtr.Zero) return false;
        Native.GetWindowRect(_hwnd, out var r);
        return x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
    }

    public void HideIcon()
    {
        _hideTimer.Stop();
        if (!IsVisible) return;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Hide();
    }

    private void RestartTimer(double seconds)
    {
        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 30));
        _hideTimer.Start();
    }
}
