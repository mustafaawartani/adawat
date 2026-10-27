using System;

namespace Adawat.Gestures;

/// <summary>
/// The math behind the screen zoom, kept free of Windows calls so it can be tested.
///
/// There are two states: the <i>target</i> (where the fingers want the view) and what is
/// <i>shown</i>, which glides toward the target a little every frame. Touchpads report finger
/// positions with some noise; feeding them straight to the magnifier made the picture tremble.
///
/// While zooming, the view is "anchored": one screen point (under the pointer) stays exactly
/// where it is on screen at every in-between zoom level, so the picture never wobbles sideways.
/// </summary>
public sealed class ZoomView
{
    public const double MinLevel = 1.0;

    /// <summary>Seconds for the shown level to cover ~63% of the way to the target while pinching.</summary>
    public double PinchSmoothing { get; set; } = 0.035;

    /// <summary>The same, for glides that are not driven by the fingers (snap, reset, following the pointer).</summary>
    public double SettleSmoothing { get; set; } = 0.085;

    /// <summary>The same, for moving the view with two fingers.</summary>
    public double PanSmoothing { get; set; } = 0.03;

    public double ScreenWidth { get; set; } = 1920;
    public double ScreenHeight { get; set; } = 1080;
    public double MaxLevel { get; set; } = 5;

    public double Level { get; private set; } = 1;
    public double X { get; private set; }
    public double Y { get; private set; }

    public double TargetLevel { get; private set; } = 1;
    public double TargetX { get; private set; }
    public double TargetY { get; private set; }

    /// <summary>True while following the fingers of a pinch (quicker smoothing).</summary>
    public bool Pinching { get; set; }

    private bool _anchored;
    private double _anchorX, _anchorY;           // screen point that stays in place (unzoomed coordinates)
    private double _anchorShownX, _anchorShownY; // where it appears on screen

    public bool IsZoomed => Level > MinLevel + 0.001 || TargetLevel > MinLevel + 0.001;

    /// <summary>True when what is shown has reached the target.</summary>
    public bool IsSettled =>
        Math.Abs(Math.Log(Level / TargetLevel)) < 0.0005 && Math.Abs(X - TargetX) < 0.05 && Math.Abs(Y - TargetY) < 0.05;

    /// <summary>Where a screen point (unzoomed coordinates) currently appears on screen.</summary>
    public (double X, double Y) Shown(double x, double y) => ((x - X) * Level, (y - Y) * Level);

    /// <summary>Keeps <paramref name="x"/>,<paramref name="y"/> fixed on screen for the zoom that follows.</summary>
    public void AnchorAt(double x, double y)
    {
        _anchored = true;
        _anchorX = x;
        _anchorY = y;
        (_anchorShownX, _anchorShownY) = Shown(x, y);
        UpdateAnchoredTarget();
    }

    /// <summary>Sets the zoom level to reach, around the current anchor.</summary>
    public void ZoomTo(double level)
    {
        TargetLevel = Math.Clamp(level, MinLevel, Math.Max(MinLevel, MaxLevel));
        if (_anchored) UpdateAnchoredTarget();
        else (TargetX, TargetY) = Clamp(TargetLevel, TargetX, TargetY);
    }

    /// <summary>Moves the target view by screen pixels (unzoomed coordinates). Ends the anchor.</summary>
    public void PanBy(double dx, double dy)
    {
        _anchored = false;
        (TargetX, TargetY) = Clamp(TargetLevel, TargetX + dx, TargetY + dy);
    }

    /// <summary>Moves the target view so its top-left corner is at <paramref name="x"/>,<paramref name="y"/>. Ends the anchor.</summary>
    public void PanTo(double x, double y)
    {
        _anchored = false;
        (TargetX, TargetY) = Clamp(TargetLevel, x, y);
    }

    /// <summary>
    /// Moves the view at once (both what is shown and the target), for following the mouse
    /// pointer: the pointer already moves smoothly, and gliding after it would lag and stutter.
    /// Ends the anchor.
    /// </summary>
    public void JumpTo(double x, double y)
    {
        _anchored = false;
        (TargetX, TargetY) = Clamp(TargetLevel, x, y);
        (X, Y) = Clamp(Level, x, y);
    }

    /// <summary>
    /// Rounds the target to a whole zoom level (2x, 3x…) when it is close to one. At whole levels
    /// every screen pixel becomes the same number of pixels, which is the sharpest picture.
    /// </summary>
    public void SnapToWholeLevel(double within = 0.3)
    {
        double whole = Math.Round(TargetLevel);
        if (whole >= 2 && Math.Abs(TargetLevel - whole) <= within) ZoomTo(whole);
    }

    /// <summary>Moves the shown state toward the target. Returns true if anything changed.</summary>
    public bool Step(double seconds)
    {
        if (IsSettled)
        {
            if (Level == TargetLevel && X == TargetX && Y == TargetY) return false;
            Level = TargetLevel;
            X = TargetX;
            Y = TargetY;
            return true;
        }

        seconds = Math.Clamp(seconds, 0.001, 0.1);
        double levelTau = Pinching ? PinchSmoothing : SettleSmoothing;
        double levelBlend = 1 - Math.Exp(-seconds / levelTau);
        // Zoom feels even when it changes by the same ratio per frame, so blend in log space.
        Level = Math.Exp(Math.Log(Level) + (Math.Log(TargetLevel) - Math.Log(Level)) * levelBlend);

        if (_anchored)
        {
            (X, Y) = Clamp(Level, _anchorX - _anchorShownX / Level, _anchorY - _anchorShownY / Level);
        }
        else
        {
            double panBlend = 1 - Math.Exp(-seconds / (Pinching ? PinchSmoothing : PanSmoothing));
            (X, Y) = Clamp(Level, X + (TargetX - X) * panBlend, Y + (TargetY - Y) * panBlend);
        }
        return true;
    }

    /// <summary>Jumps straight to normal size.</summary>
    public void Reset()
    {
        _anchored = false;
        Pinching = false;
        Level = TargetLevel = 1;
        X = Y = TargetX = TargetY = 0;
    }

    private void UpdateAnchoredTarget() =>
        (TargetX, TargetY) = Clamp(TargetLevel, _anchorX - _anchorShownX / TargetLevel, _anchorY - _anchorShownY / TargetLevel);

    private (double X, double Y) Clamp(double level, double x, double y) => (
        Math.Clamp(x, 0, Math.Max(0, ScreenWidth - ScreenWidth / level)),
        Math.Clamp(y, 0, Math.Max(0, ScreenHeight - ScreenHeight / level)));
}
