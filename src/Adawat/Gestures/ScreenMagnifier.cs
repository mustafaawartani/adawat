using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Adawat.Core;
using Adawat.Interop;

namespace Adawat.Gestures;

/// <summary>
/// Chrome-style visual zoom for the whole screen, using the Windows magnification API
/// (the same one the Windows Magnifier uses). Nothing inside apps changes size: the picture
/// on screen is magnified around the pointer, and moving the pointer pans the view.
/// Clicks keep landing where the magnified pointer shows.
/// </summary>
public sealed class ScreenMagnifier : IDisposable
{
    [DllImport("Magnification.dll", SetLastError = true)]
    private static extern bool MagInitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    private static extern bool MagUninitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    private static extern bool MagSetFullscreenTransform(float magLevel, int xOffset, int yOffset);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    private const double MinLevel = 1.0;
    private readonly DispatcherTimer _follow;
    private readonly DispatcherTimer _glide;
    private double _glideVx, _glideVy;      // screen pixels per frame
    private bool _panning;
    private bool _initialized;
    private bool _failed;

    private double _level = 1;
    private double _offsetX, _offsetY;      // top-left of the magnified area, in screen pixels

    // Pinch state
    private bool _pinching;
    private double _pinchStartLevel;
    private double _anchorX, _anchorY;      // screen point under the fingers when the pinch began
    private double _anchorShownX, _anchorShownY; // where that point appeared on screen

    public double MaxLevel { get; set; } = 5;

    /// <summary>How strongly finger movement changes the zoom (1 = proportional).</summary>
    public double Gain { get; set; } = 1.5;

    public bool IsZoomed => _level > MinLevel + 0.001;

    /// <summary>How far the view moves for a finger movement (1 = one touchpad width moves one screen width).</summary>
    public double PanSpeed { get; set; } = 1.6;

    /// <summary>False: the content follows the fingers (like Chrome). True: the view follows the fingers.</summary>
    public bool InvertPan { get; set; }

    /// <summary>Raised when the screen goes from normal size to zoomed (true) or back (false).</summary>
    public event Action<bool>? ZoomedChanged;

    public ScreenMagnifier()
    {
        _follow = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _follow.Tick += (_, _) => FollowPointer();
        _glide = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _glide.Tick += (_, _) => GlideStep();
    }

    private bool EnsureInitialized()
    {
        if (_initialized) return true;
        if (_failed) return false;
        try
        {
            _initialized = MagInitialize();
            if (!_initialized) Log.Error($"Screen zoom could not start (error {Marshal.GetLastWin32Error()})");
        }
        catch (Exception ex)
        {
            Log.Error("Screen zoom is not available", ex);
        }
        _failed = !_initialized;
        return _initialized;
    }

    private static (int Width, int Height) ScreenSize()
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? System.Windows.Forms.SystemInformation.VirtualScreen;
        return (bounds.Width, bounds.Height);
    }

    public void BeginPinch()
    {
        if (!EnsureInitialized()) return;
        var p = WindowInfo.CursorPosition();
        _pinching = true;
        _pinchStartLevel = _level;
        _anchorX = p.X;
        _anchorY = p.Y;
        _anchorShownX = (p.X - _offsetX) * _level;
        _anchorShownY = (p.Y - _offsetY) * _level;
        _follow.Stop();
        _glide.Stop();
    }

    /// <summary><paramref name="fingerScale"/>: finger distance now / at the start of the pinch.</summary>
    public void UpdatePinch(double fingerScale)
    {
        if (!_pinching) BeginPinch();
        if (!_initialized) return;

        double level = _pinchStartLevel * Math.Pow(Math.Max(fingerScale, 0.01), Gain);
        level = Math.Clamp(level, MinLevel, Math.Max(MinLevel, MaxLevel));

        // Keep the point under the fingers where it appears on screen while zooming.
        double x = _anchorX - _anchorShownX / level;
        double y = _anchorY - _anchorShownY / level;
        Apply(level, x, y);
    }

    public void EndPinch()
    {
        _pinching = false;
        if (_level < 1.06) Reset();
        else _follow.Start();
    }

    // ---- Panning with two fingers while zoomed -----------------------------------------

    /// <summary>Moves the zoomed view by a finger movement (in touchpad widths).</summary>
    public void Pan(double fingerDx, double fingerDy)
    {
        if (!IsZoomed || !_initialized) return;
        if (!_panning)
        {
            _panning = true;
            _follow.Stop();
            _glide.Stop();
        }
        var (width, _) = ScreenSize();
        // A finger movement across the touchpad moves the picture by about PanSpeed screens.
        double pixels = width * PanSpeed / _level;
        double sign = InvertPan ? 1 : -1;
        MoveView(sign * fingerDx * pixels, sign * fingerDy * pixels);
    }

    /// <summary>Fingers lifted: keep gliding a little with the finger speed (touchpad widths per second), like Chrome.</summary>
    public void EndPan(double velocityX, double velocityY)
    {
        _panning = false;
        if (!IsZoomed) return;
        var (width, _) = ScreenSize();
        double pixels = width * PanSpeed / _level;
        double sign = InvertPan ? 1 : -1;
        _glideVx = sign * velocityX * pixels / 60.0;
        _glideVy = sign * velocityY * pixels / 60.0;
        if (Math.Abs(_glideVx) + Math.Abs(_glideVy) > 1.5) _glide.Start();
        else _follow.Start();
    }

    private void GlideStep()
    {
        _glideVx *= 0.92;
        _glideVy *= 0.92;
        if (!IsZoomed || Math.Abs(_glideVx) + Math.Abs(_glideVy) < 0.5)
        {
            _glide.Stop();
            if (IsZoomed) _follow.Start();
            return;
        }
        MoveView(_glideVx, _glideVy);
    }

    /// <summary>
    /// Shifts the visible area and moves the pointer with it, so the pointer stays at the same
    /// place on screen while the picture slides underneath (and the view does not snap back to it).
    /// </summary>
    private void MoveView(double dx, double dy)
    {
        var cursor = WindowInfo.CursorPosition();
        double shownX = (cursor.X - _offsetX) * _level;
        double shownY = (cursor.Y - _offsetY) * _level;
        Apply(_level, _offsetX + dx, _offsetY + dy);
        SetCursorPos((int)Math.Round(_offsetX + shownX / _level), (int)Math.Round(_offsetY + shownY / _level));
    }

    public void Reset()
    {
        _pinching = false;
        _panning = false;
        _glide.Stop();
        _follow.Stop();
        if (_initialized) Apply(1, 0, 0);
    }

    private void Apply(double level, double x, double y)
    {
        var (width, height) = ScreenSize();
        x = Math.Clamp(x, 0, Math.Max(0, width - width / level));
        y = Math.Clamp(y, 0, Math.Max(0, height - height / level));
        bool wasZoomed = IsZoomed;
        _level = level;
        _offsetX = x;
        _offsetY = y;
        if (!MagSetFullscreenTransform((float)level, (int)Math.Round(x), (int)Math.Round(y)))
            Log.Error($"Screen zoom failed (error {Marshal.GetLastWin32Error()})");
        if (wasZoomed != IsZoomed) ZoomedChanged?.Invoke(IsZoomed);
    }

    /// <summary>While zoomed, pan so the pointer never leaves the visible area.</summary>
    private void FollowPointer()
    {
        if (!IsZoomed)
        {
            _follow.Stop();
            return;
        }
        var (width, height) = ScreenSize();
        var p = WindowInfo.CursorPosition();
        double viewW = width / _level, viewH = height / _level;
        double marginX = viewW * 0.08, marginY = viewH * 0.08;
        double x = _offsetX, y = _offsetY;
        if (p.X < x + marginX) x = p.X - marginX;
        else if (p.X > x + viewW - marginX) x = p.X - viewW + marginX;
        if (p.Y < y + marginY) y = p.Y - marginY;
        else if (p.Y > y + viewH - marginY) y = p.Y - viewH + marginY;
        if (Math.Abs(x - _offsetX) > 0.5 || Math.Abs(y - _offsetY) > 0.5) Apply(_level, x, y);
    }

    public void Dispose()
    {
        _follow.Stop();
        _glide.Stop();
        if (IsZoomed) ZoomedChanged?.Invoke(false);
        if (!_initialized) return;
        try
        {
            MagSetFullscreenTransform(1, 0, 0);
            MagUninitialize();
        }
        catch
        {
        }
        _initialized = false;
    }
}
