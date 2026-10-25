using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Adawat.Core;
using Adawat.Interop;
using Adawat.UI;

namespace Adawat.Gestures;

/// <summary>The round arrow that slides in from the edge of the window during a swipe, like in Chrome.</summary>
public sealed class SwipeArrowOverlay : Window
{
    private const double SizeDip = 52;

    private readonly Ellipse _circle;
    private readonly Path _arrow;
    private readonly ScaleTransform _scale = new(1, 1);
    private IntPtr _hwnd;
    private Native.RECT _target;
    private int _direction;
    private bool _ready;

    public SwipeArrowOverlay()
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

        _circle = new Ellipse
        {
            Width = SizeDip - 8,
            Height = SizeDip - 8,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = 0.35 },
        };
        _arrow = new Path
        {
            Data = Geometry.Parse("M 14,4 L 6,12 L 14,20 M 6,12 L 22,12"),
            StrokeThickness = 2.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 24,
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var grid = new Grid { RenderTransform = _scale, RenderTransformOrigin = new Point(0.5, 0.5) };
        grid.Children.Add(_circle);
        grid.Children.Add(_arrow);
        Content = grid;
        ApplyColors(false);

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
            ex |= Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_LAYERED;
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
        };
    }

    private void ApplyColors(bool ready)
    {
        bool dark = ThemeHelper.IsDark;
        var accent = ThemeHelper.AccentColor;
        _circle.Fill = new SolidColorBrush(ready ? accent : (dark ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Color.FromRgb(0xF3, 0xF3, 0xF3)));
        _arrow.Stroke = new SolidColorBrush(ready ? Colors.White : (dark ? Colors.White : Color.FromRgb(0x20, 0x20, 0x20)));
    }

    /// <summary>Starts showing the arrow for a swipe over <paramref name="targetWindow"/>.</summary>
    public void Begin(IntPtr targetWindow, int direction)
    {
        BeginAnimation(OpacityProperty, null);
        if (!Native.GetWindowRect(targetWindow, out _target) || _target.Width < 100)
        {
            var p = WindowInfo.CursorPosition();
            _target = WindowInfo.WorkAreaAt(p);
        }
        // Keep the arrow on the visible part of the screen.
        var work = WindowInfo.WorkAreaAt(new Native.POINT((_target.Left + _target.Right) / 2, (_target.Top + _target.Bottom) / 2));
        _target.Left = Math.Max(_target.Left, work.Left);
        _target.Right = Math.Min(_target.Right, work.Right);
        _target.Top = Math.Max(_target.Top, work.Top);
        _target.Bottom = Math.Min(_target.Bottom, work.Bottom);

        _direction = direction;
        _ready = false;
        _arrow.RenderTransform = new RotateTransform(direction > 0 ? 180 : 0);
        ApplyColors(false);
        _scale.ScaleX = _scale.ScaleY = 1;
        Opacity = 0;
        if (!IsVisible) Show();
    }

    public void Update(int direction, double progress)
    {
        if (!IsVisible) return;
        if (direction != _direction)
        {
            _direction = direction;
            _arrow.RenderTransform = new RotateTransform(direction > 0 ? 180 : 0);
        }
        double p = Math.Clamp(progress, 0, 1);
        bool ready = progress >= 1;
        if (ready != _ready)
        {
            _ready = ready;
            ApplyColors(ready);
        }
        Opacity = Math.Min(1, p * 1.6);

        double scale = WindowInfo.ScaleAt(new Native.POINT(_target.Left + 1, _target.Top + 1));
        int size = (int)(SizeDip * scale);
        int travel = (int)((SizeDip + 18) * scale * p);
        int y = (_target.Top + _target.Bottom) / 2 - size / 2;
        int x = direction < 0
            ? _target.Left - size + travel
            : _target.Right - travel;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public void End(bool committed)
    {
        if (!IsVisible) return;
        if (committed)
        {
            var grow = new DoubleAnimation(1, 1.18, TimeSpan.FromMilliseconds(120)) { AutoReverse = true };
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(committed ? 320 : 160));
        fade.Completed += (_, _) =>
        {
            if (Opacity < 0.01) Hide();
        };
        BeginAnimation(OpacityProperty, fade);
    }
}
