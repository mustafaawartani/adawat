using System;
using System.Collections.Generic;

namespace Adawat.Gestures;

/// <summary>One finger on the touchpad. X and Y are in "touchpad widths" (0..1 across, Y scaled to the same unit).</summary>
public readonly record struct TouchContact(int Id, double X, double Y);

public enum GestureEventKind
{
    /// <summary>A horizontal swipe is in progress. <see cref="GestureEvent.Progress"/> reaches 1 when it will trigger.</summary>
    SwipeUpdate,
    /// <summary>The fingers lifted. <see cref="GestureEvent.Committed"/> says whether to navigate.</summary>
    SwipeEnd,
    /// <summary>One pinch zoom step. <see cref="GestureEvent.Direction"/> is +1 for zoom in, -1 for zoom out.</summary>
    ZoomStep,
}

/// <summary>
/// <see cref="Direction"/>: for swipes, -1 = back, +1 = forward; for zoom, +1 = in, -1 = out.
/// </summary>
public readonly record struct GestureEvent(GestureEventKind Kind, int Direction, double Progress = 0, bool Committed = false, int SessionId = 0);

public sealed class GestureConfig
{
    public bool SwipeEnabled { get; set; } = true;
    public int SwipeFingers { get; set; } = 2;
    public int SwipeSensitivity { get; set; } = 50;
    public bool InvertSwipe { get; set; }
    public bool PinchEnabled { get; set; } = true;
    public int PinchSensitivity { get; set; } = 50;
    public bool InvertPinch { get; set; }

    /// <summary>Horizontal travel (in touchpad widths) needed to trigger back/forward.</summary>
    public double SwipeDistance => Lerp(0.45, 0.12, Math.Clamp(SwipeSensitivity, 1, 100) / 100.0);

    /// <summary>Finger distance ratio for one zoom step.</summary>
    public double PinchStepRatio => Lerp(1.40, 1.07, Math.Clamp(PinchSensitivity, 1, 100) / 100.0);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}

/// <summary>
/// Turns touchpad frames into browser-style gestures: a horizontal multi-finger swipe
/// (back/forward) and a two-finger pinch (zoom). Pure logic, no Windows calls, so it is unit tested.
/// </summary>
public sealed class GestureEngine
{
    private enum Mode { Undecided, Swipe, Pinch, Ignored }

    private const double DecideDistance = 0.035;   // movement before we decide what the fingers are doing
    private const double QuickFlickMs = 260;        // a fast flick needs less distance
    private const double QuickFlickProgress = 0.55;

    private readonly List<GestureEvent> _events = new();
    private Session? _session;
    private int _sessionCounter;

    public GestureConfig Config { get; set; } = new();

    public int CurrentSessionId => _session?.Id ?? 0;

    private sealed class Session
    {
        public int Id;
        public int Fingers;
        public HashSet<int> Ids = new();
        public double StartX, StartY, StartSpread, LastStepSpread;
        public long StartMs;
        public Mode Mode;
        public int SwipeDirection;
        public double SwipeProgress;
    }

    /// <summary>Feeds one frame (all fingers currently touching). Returns the resulting events.</summary>
    public IReadOnlyList<GestureEvent> Process(IReadOnlyList<TouchContact> contacts, long timestampMs)
    {
        _events.Clear();

        if (_session != null && (contacts.Count != _session.Fingers || !SameFingers(contacts, _session.Ids)))
        {
            EndSession(timestampMs);
        }

        if (contacts.Count < 2)
        {
            return _events;
        }

        if (_session == null)
        {
            StartSession(contacts, timestampMs);
            return _events;
        }

        Update(contacts, timestampMs);
        return _events;
    }

    /// <summary>Call when frames stop arriving (fingers lifted without a final empty frame).</summary>
    public IReadOnlyList<GestureEvent> Reset(long timestampMs)
    {
        _events.Clear();
        EndSession(timestampMs);
        return _events;
    }

    private static bool SameFingers(IReadOnlyList<TouchContact> contacts, HashSet<int> ids)
    {
        foreach (var c in contacts)
        {
            if (!ids.Contains(c.Id)) return false;
        }
        return true;
    }

    private void StartSession(IReadOnlyList<TouchContact> contacts, long timestampMs)
    {
        var (cx, cy) = Centroid(contacts);
        var spread = Spread(contacts, cx, cy);
        var session = new Session
        {
            Id = ++_sessionCounter,
            Fingers = contacts.Count,
            StartX = cx,
            StartY = cy,
            StartSpread = spread,
            LastStepSpread = spread,
            StartMs = timestampMs,
            Mode = Mode.Undecided,
        };
        foreach (var c in contacts) session.Ids.Add(c.Id);
        _session = session;
    }

    private void Update(IReadOnlyList<TouchContact> contacts, long timestampMs)
    {
        var s = _session!;
        var (cx, cy) = Centroid(contacts);
        var spread = Spread(contacts, cx, cy);
        double dx = cx - s.StartX;
        double dy = cy - s.StartY;
        double dSpread = spread - s.StartSpread;

        if (s.Mode == Mode.Undecided)
        {
            double move = Math.Max(Math.Abs(dx), Math.Abs(dy));
            bool canPinch = Config.PinchEnabled && s.Fingers == 2;
            bool canSwipe = Config.SwipeEnabled && s.Fingers == Config.SwipeFingers;

            if (canPinch && Math.Abs(dSpread) >= DecideDistance && Math.Abs(dSpread) > move * 0.8)
            {
                s.Mode = Mode.Pinch;
            }
            else if (canSwipe && Math.Abs(dx) >= DecideDistance && Math.Abs(dx) >= 2 * Math.Abs(dy) && Math.Abs(dSpread) < Math.Abs(dx) * 0.6)
            {
                s.Mode = Mode.Swipe;
            }
            else if (move >= DecideDistance * 1.5 || Math.Abs(dSpread) >= DecideDistance * 1.5)
            {
                // Vertical scrolling, or a gesture we do not handle.
                s.Mode = Mode.Ignored;
            }
        }

        switch (s.Mode)
        {
            case Mode.Swipe:
            {
                int direction = dx > 0 ? -1 : 1; // fingers to the right = back, like Chrome
                if (Config.InvertSwipe) direction = -direction;
                s.SwipeDirection = direction;
                s.SwipeProgress = Math.Abs(dx) / Config.SwipeDistance;
                if (Math.Abs(dy) > Math.Abs(dx) * 1.2) s.SwipeProgress = 0; // drifted into a vertical move
                _events.Add(new GestureEvent(GestureEventKind.SwipeUpdate, direction, Math.Min(s.SwipeProgress, 1.5), SessionId: s.Id));
                break;
            }
            case Mode.Pinch:
            {
                double step = Config.PinchStepRatio;
                if (s.LastStepSpread <= 1e-4) s.LastStepSpread = Math.Max(spread, 1e-4);
                double ratio = spread / s.LastStepSpread;
                if (ratio >= step || ratio <= 1 / step)
                {
                    int direction = ratio > 1 ? 1 : -1;
                    if (Config.InvertPinch) direction = -direction;
                    _events.Add(new GestureEvent(GestureEventKind.ZoomStep, direction, SessionId: s.Id));
                    s.LastStepSpread = spread;
                }
                break;
            }
        }
    }

    private void EndSession(long timestampMs)
    {
        var s = _session;
        _session = null;
        if (s == null || s.Mode != Mode.Swipe) return;

        bool quick = timestampMs - s.StartMs <= QuickFlickMs && s.SwipeProgress >= QuickFlickProgress;
        bool committed = s.SwipeProgress >= 1 || quick;
        _events.Add(new GestureEvent(GestureEventKind.SwipeEnd, s.SwipeDirection, s.SwipeProgress, committed, s.Id));
    }

    private static (double X, double Y) Centroid(IReadOnlyList<TouchContact> contacts)
    {
        double x = 0, y = 0;
        foreach (var c in contacts)
        {
            x += c.X;
            y += c.Y;
        }
        return (x / contacts.Count, y / contacts.Count);
    }

    /// <summary>Average finger distance from the centroid, times two (for two fingers: their distance).</summary>
    private static double Spread(IReadOnlyList<TouchContact> contacts, double cx, double cy)
    {
        double sum = 0;
        foreach (var c in contacts)
        {
            sum += Math.Sqrt((c.X - cx) * (c.X - cx) + (c.Y - cy) * (c.Y - cy));
        }
        return 2 * sum / contacts.Count;
    }
}
