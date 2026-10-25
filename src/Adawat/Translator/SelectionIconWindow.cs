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
    private const double SizeDip = 30;
    private readonly DispatcherTimer _hideTimer;
    private readonly Border _badge;
    private IntPtr _hwnd;

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
        Width = SizeDip + 8;
        Height = SizeDip + 8;
        Left = -10000;
        Top = -10000;
        Cursor = Cursors.Hand;
        ToolTip = "Translate (Adawat)";

        var label = new Grid();
        label.Children.Add(new TextBlock
        {
            Text = "A",
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = Brushes.White,
            Margin = new Thickness(5, 2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        });
        label.Children.Add(new TextBlock
        {
            Text = "ع",
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 0, 6, 1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
        });

        _badge = new Border
        {
            Width = SizeDip,
            Height = SizeDip,
            CornerRadius = new CornerRadius(8),
            Background = new LinearGradientBrush(Color.FromRgb(0x25, 0x63, 0xEB), Color.FromRgb(0x7C, 0x3A, 0xED), 90),
            Child = label,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.35 },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
        };
        Content = _badge;

        _hideTimer = new DispatcherTimer();
        _hideTimer.Tick += (_, _) => HideIcon();

        MouseEnter += (_, _) => _hideTimer.Stop();
        MouseLeave += (_, _) => RestartTimer(1.5);
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

    /// <summary>Shows the icon near the physical screen point where the selection ended.</summary>
    public void ShowAt(Native.POINT p, double seconds)
    {
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }
        double scale = WindowInfo.ScaleAt(p);
        int size = (int)((SizeDip + 8) * scale);
        var work = WindowInfo.WorkAreaAt(p);
        int x = p.X + (int)(10 * scale);
        int y = p.Y - size - (int)(6 * scale);
        if (y < work.Top) y = p.Y + (int)(18 * scale);
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - size));
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        var pop = new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new BackEase { Amplitude = 0.4 } };
        ((ScaleTransform)_badge.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        ((ScaleTransform)_badge.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        RestartTimer(seconds);
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
