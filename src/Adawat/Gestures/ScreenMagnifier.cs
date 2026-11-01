using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using Adawat.Core;
using Adawat.Interop;

namespace Adawat.Gestures;

/// <summary>
/// Chrome-style visual zoom for the whole screen, using the Windows magnification API
/// (the same one the Windows Magnifier uses). Nothing inside apps changes size: the picture
/// on screen is magnified around the pointer. Two fingers move the view; the mouse pointer stays
/// inside the zoomed view (or, if chosen, pushes the view along at its edges).
/// Clicks keep landing where the magnified pointer shows.
///
/// The zoom is drawn by its own thread in step with the screen refresh (see <see cref="ZoomView"/>
/// for the smoothing), so it stays fluid even while the app is busy.
/// </summary>
public sealed class ScreenMagnifier : IDisposable
{
    [DllImport("Magnification.dll", SetLastError = true)]
    private static extern bool MagInitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    private static extern bool MagUninitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    private static extern bool MagSetFullscreenTransform(float magLevel, int xOffset, int yOffset);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool ClipCursor(ref RECT rect);

    [DllImport("user32.dll", EntryPoint = "ClipCursor")]
    private static extern bool UnclipCursor(IntPtr none);

    /// <summary>Not in the documentation, but exported since Windows 10: smooth (true) or sharp pixels (false).</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool SetSmoothingProc([MarshalAs(UnmanagedType.Bool)] bool smooth);

    private readonly object _gate = new();
    private readonly ZoomView _view = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private Thread? _thread;
    private volatile bool _stopping;
    private bool _failed;

    // State shared with the frame thread (guarded by _gate)
    private bool _panning, _gliding;
    private double _glideVx, _glideVy;              // screen pixels per second
    private double _cursorShownX, _cursorShownY;    // where the pointer stays on screen while panning
    private bool _holdCursor;
    private bool _smoothingWanted;
    private bool? _smoothingApplied;
    private bool _wasZoomed;
    private double _scrollX, _scrollY;               // movement past the screen edge, still to scroll (screen pixels)
    private double _wheelRestX, _wheelRestY;         // wheel units not sent yet

    /// <summary>
    /// True: moving past the edge of the zoomed screen goes on to scroll the app, like Chrome.
    /// False: the view simply stops at the edge.
    /// </summary>
    public bool ScrollPastEdges { get; set; } = true;

    public double MaxLevel { get => _view.MaxLevel; set { lock (_gate) _view.MaxLevel = value; } }

    /// <summary>How strongly finger movement changes the zoom (1 = proportional).</summary>
    public double Gain { get; set; } = 1.5;

    /// <summary>How far the view moves for a finger movement (1 = one touchpad width moves one screen width).</summary>
    public double PanSpeed { get; set; } = 1.6;

    /// <summary>False: the content follows the fingers (like Chrome). True: the view follows the fingers.</summary>
    public bool InvertPan { get; set; }

    /// <summary>
    /// True: pushing the pointer against the edge of the zoomed view moves the view along.
    /// False: the pointer stops at the edge of what is shown, and only two fingers move the view.
    /// </summary>
    public bool PointerMovesView { get; set; }

    /// <summary>Round the zoom to 2x, 3x… when the fingers lift close to one, for the sharpest picture.</summary>
    public bool SnapToWholeLevels { get; set; }

    /// <summary>True: soften the enlarged pixels (smoother edges, a little blurry). False: crisp pixels.</summary>
    public bool SmoothEdges
    {
        get => _smoothingWanted;
        set
        {
            lock (_gate) _smoothingWanted = value;
            _wake.Set();
        }
    }

    public bool IsZoomed
    {
        get { lock (_gate) return _view.IsZoomed; }
    }

    /// <summary>Raised on the UI thread when the screen goes from normal size to zoomed (true) or back (false).</summary>
    public event Action<bool>? ZoomedChanged;

    private double _pinchStartLevel;

    /// <summary><paramref name="fingerScale"/>: finger distance now / at the start of the pinch.</summary>
    public void UpdatePinch(double fingerScale)
    {
        if (!EnsureRunning()) return;
        lock (_gate)
        {
            if (!_view.Pinching)
            {
                // Start of a pinch: the point under the pointer stays where it is on screen.
                var p = WindowInfo.CursorPosition();
                _view.Pinching = true;
                _panning = _gliding = _holdCursor = false;
                _pinchStartLevel = _view.TargetLevel;
                UpdateScreenSize();
                _view.AnchorAt(p.X, p.Y);
            }
            _view.ZoomTo(_pinchStartLevel * Math.Pow(Math.Max(fingerScale, 0.01), Gain));
        }
        _wake.Set();
    }

    public void EndPinch()
    {
        lock (_gate)
        {
            if (!_view.Pinching) return;
            _view.Pinching = false;
            if (_view.TargetLevel < 1.1) _view.ZoomTo(1);
            else if (SnapToWholeLevels) _view.SnapToWholeLevel();
        }
        _wake.Set();
    }

    // ---- Panning with two fingers while zoomed -----------------------------------------

    /// <summary>Moves the zoomed view by a finger movement (in touchpad widths).</summary>
    public void Pan(double fingerDx, double fingerDy)
    {
        lock (_gate)
        {
            if (!_view.IsZoomed) return;
            if (!_panning) BeginHoldingCursor();
            _panning = true;
            _gliding = false;
            double pixels = _view.ScreenWidth * PanSpeed / _view.TargetLevel;
            double sign = InvertPan ? 1 : -1;
            double dx = sign * fingerDx * pixels, dy = sign * fingerDy * pixels;
            var (overX, overY) = _view.PanBy(dx, dy);
            AddScroll(dx, dy, overX, overY);
        }
        FlushScroll();
        _wake.Set();
    }

    /// <summary>Keeps the movement that went past the edge, to scroll the app with it (under the lock).</summary>
    private void AddScroll(double dx, double dy, double overX, double overY)
    {
        if (!ScrollPastEdges) return;
        // Only along the direction the fingers really go: a slight sideways drift while scrolling
        // down must not scroll the page sideways.
        if (Math.Abs(overX) > 0.01 && Math.Abs(dx) >= Math.Abs(dy) * 0.5) _scrollX += overX;
        if (Math.Abs(overY) > 0.01 && Math.Abs(dy) >= Math.Abs(dx) * 0.5) _scrollY += overY;
    }

    /// <summary>
    /// Sends the movement past the edge to the app under the pointer as smooth wheel scrolling, so the
    /// page keeps moving under the fingers exactly where the zoomed view stopped, like Chrome.
    /// </summary>
    private void FlushScroll()
    {
        int wheelX, wheelY;
        lock (_gate)
        {
            if (_scrollX == 0 && _scrollY == 0) return;
            // Browsers scroll about 100 DIPs for one wheel notch (120 units).
            double unitsPerPixel = 1.2 / Math.Max(1, WindowInfo.ScaleAt(WindowInfo.CursorPosition()));
            _wheelRestY += -_scrollY * unitsPerPixel; // the view wants to go down: scroll the page down
            _wheelRestX += _scrollX * unitsPerPixel;
            _scrollX = _scrollY = 0;
            wheelY = TakeWhole(ref _wheelRestY);
            wheelX = TakeWhole(ref _wheelRestX);
        }
        if (wheelY != 0) InputSender.Wheel(wheelY, horizontal: false);
        if (wheelX != 0) InputSender.Wheel(wheelX, horizontal: true);
    }

    /// <summary>Whole wheel units of at least a small step (some apps treat every wheel message as a full notch).</summary>
    private static int TakeWhole(ref double rest)
    {
        if (Math.Abs(rest) < 10) return 0;
        int whole = (int)Math.Truncate(rest);
        rest -= whole;
        return whole;
    }

    /// <summary>Fingers lifted: keep gliding a little with the finger speed (touchpad widths per second), like Chrome.</summary>
    public void EndPan(double velocityX, double velocityY)
    {
        lock (_gate)
        {
            _panning = false;
            if (!_view.IsZoomed) return;
            _wheelRestX = _wheelRestY = 0;
            double pixels = _view.ScreenWidth * PanSpeed / _view.TargetLevel;
            double sign = InvertPan ? 1 : -1;
            _glideVx = sign * velocityX * pixels;
            _glideVy = sign * velocityY * pixels;
            _gliding = Math.Abs(_glideVx) + Math.Abs(_glideVy) > 90;
        }
        _wake.Set();
    }

    /// <summary>Remembers where the pointer is on screen, so it stays there while the picture slides under it.</summary>
    private void BeginHoldingCursor()
    {
        var p = WindowInfo.CursorPosition();
        (_cursorShownX, _cursorShownY) = _view.Shown(p.X, p.Y);
        _holdCursor = true;
    }

    public void Reset()
    {
        lock (_gate)
        {
            _panning = _gliding = _holdCursor = false;
            _view.Pinching = false;
            _view.PanTo(0, 0);
            _view.ZoomTo(1);
        }
        _wake.Set();
    }

    // ---- The frame thread ---------------------------------------------------------------

    private bool EnsureRunning()
    {
        if (_thread != null) return !_failed;
        if (_failed) return false;
        var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready))
        {
            IsBackground = true,
            Name = "Adawat screen zoom",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
        ready.Wait(2000);
        return !_failed;
    }

    private void Run(ManualResetEventSlim ready)
    {
        // The magnification API belongs to the thread that initialized it, so all calls are made here.
        try
        {
            if (!MagInitialize())
            {
                Log.Error($"Screen zoom could not start (error {Marshal.GetLastWin32Error()})");
                _failed = true;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Screen zoom is not available", ex);
            _failed = true;
        }
        ready.Set();
        if (_failed) return;

        var setSmoothing = LoadSmoothingSwitch();
        var clock = Stopwatch.StartNew();
        double last = clock.Elapsed.TotalSeconds;
        int appliedX = 0, appliedY = 0;
        float appliedLevel = 1;

        while (!_stopping)
        {
            bool active;
            lock (_gate) active = _view.IsZoomed || !_view.IsSettled || _smoothingApplied != _smoothingWanted;
            if (!active)
            {
                ReleasePointer();
                _wake.WaitOne();
                last = clock.Elapsed.TotalSeconds;
                continue;
            }

            WaitForNextFrame(clock, last);
            double now = clock.Elapsed.TotalSeconds;
            double dt = now - last;
            last = now;

            float level;
            int x, y;
            bool zoomed;
            bool smooth;
            bool confine = false;
            lock (_gate)
            {
                smooth = _smoothingWanted;
                bool resting = !_gliding && !_panning && !_view.Pinching && _view.IsZoomed && Math.Abs(_view.Level - _view.TargetLevel) < 0.0005;
                if (_gliding) Glide(dt);
                else if (resting && PointerMovesView) FollowPointer();
                confine = resting && !PointerMovesView && _view.IsSettled;
                _view.Step(dt);
                level = (float)_view.Level;
                bool settled = _view.IsSettled;
                x = Hold(_view.X, appliedX, settled);
                y = Hold(_view.Y, appliedY, settled);
                zoomed = _view.IsZoomed;
                if (_holdCursor && (_panning || _gliding))
                {
                    SetCursorPos((int)Math.Round(_view.X + _cursorShownX / _view.Level), (int)Math.Round(_view.Y + _cursorShownY / _view.Level));
                }
                else
                {
                    _holdCursor = false;
                }
            }

            FlushScroll();
            if (confine) ConfinePointer(x, y, level);
            else ReleasePointer();
            if (setSmoothing != null && _smoothingApplied != smooth)
            {
                try { setSmoothing(smooth); } catch { }
                _smoothingApplied = smooth;
            }
            if (level != appliedLevel || x != appliedX || y != appliedY)
            {
                if (!MagSetFullscreenTransform(level, x, y)) Log.Error($"Screen zoom failed (error {Marshal.GetLastWin32Error()})");
                appliedLevel = level;
                appliedX = x;
                appliedY = y;
            }
            if (zoomed != _wasZoomed)
            {
                _wasZoomed = zoomed;
                _dispatcher.BeginInvoke(() => ZoomedChanged?.Invoke(zoomed));
            }
        }

        ReleasePointer();
        try
        {
            MagSetFullscreenTransform(1, 0, 0);
            MagUninitialize();
        }
        catch
        {
        }
    }

    /// <summary>
    /// The magnifier only takes whole-pixel offsets, and one pixel of offset moves the picture by
    /// the zoom level on screen (3 pixels at 3x). Plain rounding flips back and forth when the value
    /// hovers near a half pixel, which made the picture shake; keep the previous pixel until the
    /// value has clearly moved on. Once settled, use the exact nearest pixel.
    /// </summary>
    private static int Hold(double value, int previous, bool settled) =>
        settled || Math.Abs(value - previous) >= 0.8 ? (int)Math.Round(value) : previous;

    /// <summary>Waits for the next screen refresh, so every frame shows one zoom step.</summary>
    private static void WaitForNextFrame(Stopwatch clock, double last)
    {
        bool waited = false;
        try { waited = DwmFlush() == 0; } catch { }
        double since = clock.Elapsed.TotalSeconds - last;
        // DwmFlush can return at once (nothing else on screen changed); never spin faster than ~120 fps.
        if (!waited || since < 0.004) Thread.Sleep(Math.Max(1, (int)((0.008 - since) * 1000)));
    }

    private static SetSmoothingProc? LoadSmoothingSwitch()
    {
        try
        {
            var module = LoadLibrary("Magnification.dll");
            var proc = module == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(module, "MagSetFullscreenUseBitmapSmoothing");
            return proc == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<SetSmoothingProc>(proc);
        }
        catch
        {
            return null;
        }
    }

    private void Glide(double dt)
    {
        // Slow down like Chrome's fling: about 8% speed lost per 1/60 s.
        double decay = Math.Pow(0.92, dt * 60);
        _glideVx *= decay;
        _glideVy *= decay;
        if (!_view.IsZoomed || Math.Abs(_glideVx) + Math.Abs(_glideVy) < 30)
        {
            _gliding = false;
            return;
        }
        double dx = _glideVx * dt, dy = _glideVy * dt;
        var (overX, overY) = _view.PanBy(dx, dy);
        AddScroll(dx, dy, overX, overY);
    }

    private bool _clipped;

    /// <summary>
    /// Keeps the mouse pointer inside the part of the screen that is shown, like a wall at the edge of
    /// the zoomed view: the pointer cannot get lost outside it, and the view stays where the fingers
    /// put it. Applied again every frame, because Windows drops the limit when windows change.
    /// The pointer stays free on other screens (the zoom is on the main screen only).
    /// </summary>
    private void ConfinePointer(int x, int y, float level)
    {
        var p = WindowInfo.CursorPosition();
        if (!_clipped && (p.X < 0 || p.Y < 0 || p.X >= _view.ScreenWidth || p.Y >= _view.ScreenHeight)) return;
        var rect = new RECT
        {
            Left = x,
            Top = y,
            Right = x + Math.Max(1, (int)Math.Floor(_view.ScreenWidth / level)),
            Bottom = y + Math.Max(1, (int)Math.Floor(_view.ScreenHeight / level)),
        };
        _clipped = ClipCursor(ref rect);
    }

    private void ReleasePointer()
    {
        if (!_clipped) return;
        _clipped = false;
        UnclipCursor(IntPtr.Zero);
    }

    /// <summary>While zoomed, glide the view along so the pointer never leaves the visible area.</summary>
    private void FollowPointer()
    {
        var p = WindowInfo.CursorPosition();
        double viewW = _view.ScreenWidth / _view.Level, viewH = _view.ScreenHeight / _view.Level;
        double marginX = viewW * 0.08, marginY = viewH * 0.08;
        double x = _view.X, y = _view.Y;
        if (p.X < x + marginX) x = p.X - marginX;
        else if (p.X > x + viewW - marginX) x = p.X - viewW + marginX;
        if (p.Y < y + marginY) y = p.Y - marginY;
        else if (p.Y > y + viewH - marginY) y = p.Y - viewH + marginY;
        if (Math.Abs(x - _view.X) > 0.5 || Math.Abs(y - _view.Y) > 0.5) _view.JumpTo(x, y);
    }

    private void UpdateScreenSize()
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? System.Windows.Forms.SystemInformation.VirtualScreen;
        _view.ScreenWidth = bounds.Width;
        _view.ScreenHeight = bounds.Height;
    }

    public void Dispose()
    {
        _stopping = true;
        _wake.Set();
        _thread?.Join(1000);
        // Right away rather than through the dispatcher: the app is closing and must give back two-finger scrolling now.
        if (_wasZoomed)
        {
            _wasZoomed = false;
            ZoomedChanged?.Invoke(false);
        }
    }
}
