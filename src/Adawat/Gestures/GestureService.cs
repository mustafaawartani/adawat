using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using Adawat.Core;
using Adawat.Interop;

namespace Adawat.Gestures;

/// <summary>
/// Connects the touchpad reader, the gesture engine and the actions: back/forward on a
/// horizontal swipe and zoom on a pinch, in whatever app is under the mouse pointer.
/// </summary>
public sealed class GestureService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly TouchpadReader _reader;
    private readonly GestureEngine _engine = new();
    private readonly DispatcherTimer _idleTimer;
    private readonly ScreenMagnifier _magnifier = new();
    private SwipeArrowOverlay? _overlay;

    private int _targetSession = -1;
    private IntPtr _targetWindow;
    private string _targetProcess = "";
    private bool _sessionBlocked;
    private bool _overlayShown;
    private long _lastNativeWheelMs;
    private long _lastFrameMs;
    private volatile bool _zoomed;
    private bool _scrollPaused;
    private int _lastContactCount;
    private bool _windowsPinchPaused;
    private readonly bool _windowsPinchAtStart;

    public bool IsRunning { get; private set; }

    public GestureService(AppSettings settings, MessageWindow window)
    {
        _settings = settings;
        _reader = new TouchpadReader(window);
        _reader.Frame += OnFrame;
        _magnifier.ZoomedChanged += OnZoomedChanged;

        // Adawat may have been closed while zoomed: give back two-finger scrolling.
        if (settings.Gestures.ScrollPausedByAdawat)
        {
            WindowsTouchpad.SetTwoFingerScroll(true);
            settings.Gestures.ScrollPausedByAdawat = false;
        }
        // Earlier versions switched Windows' pinch off and saved that, so it kept turning itself
        // off even after the user switched it back on. Give back what the user had.
        if (settings.Gestures.WindowsPinchBefore == true) WindowsTouchpad.SetPinchZoom(true);
        settings.Gestures.WindowsPinchBefore = null;
        _windowsPinchAtStart = WindowsTouchpad.SavedPinchZoom() ?? WindowsTouchpad.IsPinchZoomEnabled() ?? true;
        RestoreWindowsPinch(force: true); // in case Adawat closed while it was paused
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _idleTimer.Tick += (_, _) =>
        {
            // Some touchpads simply stop reporting when the fingers lift.
            if (Environment.TickCount64 - _lastFrameMs > 250)
            {
                _idleTimer.Stop();
                UpdateWindowsPinch(0);
                Handle(_engine.Reset(Environment.TickCount64));
            }
        };
    }

    /// <summary>Starts or stops reading the touchpad to match the settings.</summary>
    public void Apply()
    {
        var g = _settings.Gestures;
        _engine.Config = new GestureConfig
        {
            SwipeEnabled = g.SwipeEnabled,
            SwipeFingers = Math.Clamp(g.SwipeFingers, 2, 4),
            SwipeSensitivity = g.SwipeSensitivity,
            InvertSwipe = g.InvertSwipe,
            PinchEnabled = g.PinchEnabled,
            PinchSensitivity = g.PinchSensitivity,
            InvertPinch = g.InvertPinch,
        };

        _magnifier.MaxLevel = Math.Clamp(g.MaxScreenZoom, 1.5, 10);
        _magnifier.Gain = 0.8 + 1.7 * Math.Clamp(g.PinchSensitivity, 1, 100) / 100.0;
        _magnifier.PanSpeed = 0.6 + 2.4 * Math.Clamp(g.PanSpeed, 1, 100) / 100.0;
        _magnifier.InvertPan = g.InvertPan;
        _magnifier.SnapToWholeLevels = g.SnapZoomLevels;
        _magnifier.SmoothEdges = g.SmoothZoomEdges;
        bool screenZoom = g.Enabled && g.PinchEnabled && g.PinchAction == PinchAction.ScreenZoom;
        if (!screenZoom) _magnifier.Reset();
        if (!screenZoom || !g.TurnOffWindowsPinch) RestoreWindowsPinch();

        bool wanted = g.Enabled && (g.SwipeEnabled || g.PinchEnabled);
        if (wanted && !IsRunning) IsRunning = _reader.Start();
        else if (!wanted && IsRunning)
        {
            _reader.Stop();
            IsRunning = false;
            Handle(_engine.Reset(Environment.TickCount64));
        }
    }

    /// <summary>
    /// Windows' own pinch makes apps change icon or text size, which would happen on top of the
    /// screen zoom. Instead of switching it off for good, Adawat pauses it only while fingers are on
    /// the touchpad over an app where the screen zoom works, and puts the user's setting back when
    /// they lift. Apps in the "pinching does nothing" list keep Windows' pinch, and the Windows
    /// setting itself is never changed.
    /// </summary>
    private void UpdateWindowsPinch(int contacts)
    {
        var g = _settings.Gestures;
        bool touchStarted = contacts > 0 && _lastContactCount == 0;
        bool secondFinger = contacts >= 2 && _lastContactCount < 2;
        _lastContactCount = contacts;
        if (contacts == 0)
        {
            RestoreWindowsPinch();
            return;
        }
        if (!touchStarted && !secondFinger) return;

        bool pause = false;
        if (g.Enabled && g.PinchEnabled && g.PinchAction == PinchAction.ScreenZoom && g.TurnOffWindowsPinch && UserWantsWindowsPinch())
        {
            var window = WindowInfo.RootWindowAt(WindowInfo.CursorPosition());
            pause = window != IntPtr.Zero
                    && !WindowInfo.ListContains(g.PinchDisabledApps, WindowInfo.ProcessName(window))
                    && !(g.DisableInFullscreen && WindowInfo.IsFullscreenAppRunning());
        }
        if (pause && !_windowsPinchPaused) _windowsPinchPaused = WindowsTouchpad.SetPinchZoomForNow(false);
        else if (!pause) RestoreWindowsPinch();
    }

    private bool UserWantsWindowsPinch() => WindowsTouchpad.SavedPinchZoom() ?? _windowsPinchAtStart;

    private void RestoreWindowsPinch(bool force = false)
    {
        if (!_windowsPinchPaused && !force) return;
        _windowsPinchPaused = false;
        if (UserWantsWindowsPinch()) WindowsTouchpad.SetPinchZoomForNow(true);
    }

    public void ResetZoom() => _magnifier.Reset();

    /// <summary>
    /// While zoomed, two fingers move the zoom instead of scrolling the app underneath, like Chrome.
    /// Windows' two-finger scrolling is paused (not saved, so a restart always brings it back) and
    /// put back when the zoom returns to normal size.
    /// </summary>
    private void OnZoomedChanged(bool zoomed)
    {
        _zoomed = zoomed;
        var g = _settings.Gestures;
        if (zoomed)
        {
            if (WindowsTouchpad.IsTwoFingerScrollEnabled() == true && WindowsTouchpad.SetTwoFingerScroll(false))
            {
                _scrollPaused = true;
                g.ScrollPausedByAdawat = true;
                SettingsStore.Save(_settings);
            }
        }
        else if (_scrollPaused)
        {
            WindowsTouchpad.SetTwoFingerScroll(true);
            _scrollPaused = false;
            g.ScrollPausedByAdawat = false;
            SettingsStore.Save(_settings);
        }
    }

    /// <summary>
    /// Called on the mouse hook thread for each real wheel event. While zoomed, scroll events made
    /// by the touchpad (fingers on it right now) are swallowed so the app does not scroll; a real
    /// mouse wheel still works.
    /// </summary>
    public bool ShouldBlockWheel() => _zoomed && Environment.TickCount64 - Interlocked.Read(ref _lastFrameMs) < 150;

    /// <summary>Called by the mouse hook (any thread) for real wheel events, to detect apps where Windows already zooms on pinch.</summary>
    public void NotifyNativeWheel() => _lastNativeWheelMs = Environment.TickCount64;

    private void OnFrame(IReadOnlyList<TouchContact> contacts)
    {
        Interlocked.Exchange(ref _lastFrameMs, Environment.TickCount64);
        UpdateWindowsPinch(contacts.Count);
        _engine.Config.PanMode = _magnifier.IsZoomed && _settings.Gestures.PinchAction == PinchAction.ScreenZoom;
        if (contacts.Count > 0 && !_idleTimer.IsEnabled) _idleTimer.Start();
        Handle(_engine.Process(contacts, _lastFrameMs));
    }

    private void Handle(IReadOnlyList<GestureEvent> events)
    {
        foreach (var e in events)
        {
            try
            {
                switch (e.Kind)
                {
                    case GestureEventKind.SwipeUpdate:
                        if (!EnsureTarget(e.SessionId, swipe: true)) break;
                        if (_settings.Gestures.ShowSwipeArrow)
                        {
                            _overlay ??= new SwipeArrowOverlay();
                            if (!_overlayShown)
                            {
                                _overlay.Begin(_targetWindow, e.Direction);
                                _overlayShown = true;
                            }
                            _overlay.Update(e.Direction, e.Progress);
                        }
                        break;

                    case GestureEventKind.SwipeEnd:
                        if (_overlayShown)
                        {
                            _overlay?.End(e.Committed && !_sessionBlocked);
                            _overlayShown = false;
                        }
                        if (e.Committed && EnsureTarget(e.SessionId, swipe: true)) Navigate(e.Direction > 0);
                        break;

                    case GestureEventKind.PinchUpdate:
                        if (_settings.Gestures.PinchAction != PinchAction.ScreenZoom) break;
                        if (!EnsureTarget(e.SessionId, swipe: false)) break;
                        _magnifier.UpdatePinch(e.Progress);
                        break;

                    case GestureEventKind.PinchEnd:
                        if (_settings.Gestures.PinchAction == PinchAction.ScreenZoom) _magnifier.EndPinch();
                        break;

                    case GestureEventKind.PanUpdate:
                        _magnifier.Pan(e.DeltaX, e.DeltaY);
                        break;

                    case GestureEventKind.PanEnd:
                        _magnifier.EndPan(e.DeltaX, e.DeltaY);
                        break;

                    case GestureEventKind.ZoomStep:
                        if (_settings.Gestures.PinchAction == PinchAction.ScreenZoom) break;
                        if (!EnsureTarget(e.SessionId, swipe: false)) break;
                        // Windows turns a pinch into Ctrl+wheel for some apps by itself. While those
                        // wheel events keep arriving, zooming too would double every step.
                        if (_settings.Gestures.SkipNativePinchApps && Environment.TickCount64 - _lastNativeWheelMs < 300) break;
                        Zoom(e.Direction > 0);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Gesture action failed", ex);
            }
        }
    }

    /// <summary>Resolves the app under the pointer once per gesture and checks it is allowed.</summary>
    private bool EnsureTarget(int sessionId, bool swipe)
    {
        if (_targetSession != sessionId)
        {
            _targetSession = sessionId;
            var cursor = WindowInfo.CursorPosition();
            _targetWindow = WindowInfo.RootWindowAt(cursor);
            _targetProcess = WindowInfo.ProcessName(_targetWindow);
            _sessionBlocked = _settings.Gestures.DisableInFullscreen && WindowInfo.IsFullscreenAppRunning();
        }
        if (_sessionBlocked || _targetWindow == IntPtr.Zero) return false;
        var list = swipe ? _settings.Gestures.SwipeDisabledApps : _settings.Gestures.PinchDisabledApps;
        return !WindowInfo.ListContains(list, _targetProcess);
    }

    private void Navigate(bool forward)
    {
        switch (_settings.Gestures.SwipeAction)
        {
            case SwipeAction.AltArrows:
                Native.SetForegroundWindow(_targetWindow);
                InputSender.Chord(InputSender.VK_MENU, forward ? InputSender.VK_RIGHT : InputSender.VK_LEFT);
                break;
            case SwipeAction.BrowserKeys:
                Native.SetForegroundWindow(_targetWindow);
                InputSender.Tap(forward ? InputSender.VK_BROWSER_FORWARD : InputSender.VK_BROWSER_BACK);
                break;
            default:
                // Mouse back/forward buttons go to the window under the pointer and work in most apps.
                InputSender.MouseXButton(forward);
                break;
        }
    }

    private void Zoom(bool zoomIn)
    {
        switch (_settings.Gestures.PinchAction)
        {
            case PinchAction.CtrlPlusMinus:
                InputSender.Chord(InputSender.VK_CONTROL, zoomIn ? InputSender.VK_OEM_PLUS : InputSender.VK_OEM_MINUS);
                break;
            default:
                InputSender.CtrlWheel(zoomIn ? 120 : -120);
                break;
        }
    }

    public void Dispose()
    {
        _idleTimer.Stop();
        RestoreWindowsPinch();
        _reader.Dispose();
        _magnifier.Dispose();
        _overlay?.Close();
    }
}
