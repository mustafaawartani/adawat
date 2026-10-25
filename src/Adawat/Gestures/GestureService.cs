using System;
using System.Collections.Generic;
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

    public bool IsRunning { get; private set; }

    /// <summary>Null when Adawat is not managing Windows' pinch; otherwise whether turning it off took effect right away.</summary>
    public bool? WindowsPinchOffApplied { get; private set; }

    public GestureService(AppSettings settings, MessageWindow window)
    {
        _settings = settings;
        _reader = new TouchpadReader(window);
        _reader.Frame += OnFrame;
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _idleTimer.Tick += (_, _) =>
        {
            // Some touchpads simply stop reporting when the fingers lift.
            if (Environment.TickCount64 - _lastFrameMs > 250)
            {
                _idleTimer.Stop();
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
        bool screenZoom = g.Enabled && g.PinchEnabled && g.PinchAction == PinchAction.ScreenZoom;
        if (!screenZoom) _magnifier.Reset();
        ApplyWindowsPinch(screenZoom && g.TurnOffWindowsPinch);

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
    /// Windows' own pinch makes apps change icon/font size. Screen zoom turns it off (remembering
    /// the old value) and puts it back when screen zoom is no longer used.
    /// </summary>
    private void ApplyWindowsPinch(bool turnOff)
    {
        var g = _settings.Gestures;
        if (turnOff)
        {
            var current = WindowsTouchpad.IsPinchZoomEnabled();
            g.WindowsPinchBefore ??= current ?? true;
            if (current != false) WindowsPinchOffApplied = WindowsTouchpad.SetPinchZoom(false);
            else WindowsPinchOffApplied ??= true;
        }
        else
        {
            if (g.WindowsPinchBefore == true) WindowsTouchpad.SetPinchZoom(true);
            g.WindowsPinchBefore = null;
            WindowsPinchOffApplied = null;
        }
    }

    public void ResetZoom() => _magnifier.Reset();

    /// <summary>Called by the mouse hook (any thread) for real wheel events, to detect apps where Windows already zooms on pinch.</summary>
    public void NotifyNativeWheel() => _lastNativeWheelMs = Environment.TickCount64;

    private void OnFrame(IReadOnlyList<TouchContact> contacts)
    {
        _lastFrameMs = Environment.TickCount64;
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
        _reader.Dispose();
        _magnifier.Dispose();
        _overlay?.Close();
    }
}
