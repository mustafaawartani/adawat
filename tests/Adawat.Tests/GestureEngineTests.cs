using Adawat.Gestures;
using Xunit;

namespace Adawat.Tests;

public class GestureEngineTests
{
    private static TouchContact[] Two(double x1, double y1, double x2, double y2) =>
        new[] { new TouchContact(1, x1, y1), new TouchContact(2, x2, y2) };

    private static List<GestureEvent> Run(GestureEngine engine, IEnumerable<(TouchContact[] Frame, long Ms)> frames)
    {
        var events = new List<GestureEvent>();
        foreach (var (frame, ms) in frames) events.AddRange(engine.Process(frame, ms));
        return events;
    }

    /// <summary>Two fingers moving horizontally from x to x+dx over the given time.</summary>
    private static IEnumerable<(TouchContact[], long)> Swipe(double dx, long durationMs, int steps = 20)
    {
        for (int i = 0; i <= steps; i++)
        {
            double x = 0.3 + dx * i / steps;
            yield return (Two(x, 0.3, x + 0.1, 0.32), 1000 + durationMs * i / steps);
        }
        yield return (Array.Empty<TouchContact>(), 1000 + durationMs + 10);
    }

    [Fact]
    public void LongSwipeRightGoesBack()
    {
        var events = Run(new GestureEngine(), Swipe(0.4, 600));
        var end = Assert.Single(events, e => e.Kind == GestureEventKind.SwipeEnd);
        Assert.True(end.Committed);
        Assert.Equal(-1, end.Direction);
        Assert.Contains(events, e => e.Kind == GestureEventKind.SwipeUpdate && e.Progress >= 1);
    }

    [Fact]
    public void LongSwipeLeftGoesForward()
    {
        var events = Run(new GestureEngine(), Swipe(-0.4, 600));
        var end = Assert.Single(events, e => e.Kind == GestureEventKind.SwipeEnd);
        Assert.True(end.Committed);
        Assert.Equal(1, end.Direction);
    }

    [Fact]
    public void InvertSwipeFlipsDirection()
    {
        var engine = new GestureEngine { Config = new GestureConfig { InvertSwipe = true } };
        var end = Assert.Single(Run(engine, Swipe(0.4, 600)), e => e.Kind == GestureEventKind.SwipeEnd);
        Assert.Equal(1, end.Direction);
    }

    [Fact]
    public void ShortSlowSwipeIsCancelled()
    {
        var end = Assert.Single(Run(new GestureEngine(), Swipe(0.1, 800)), e => e.Kind == GestureEventKind.SwipeEnd);
        Assert.False(end.Committed);
    }

    [Fact]
    public void QuickFlickCommitsWithLessDistance()
    {
        // 0.2 is below the default distance (~0.285) but done in 150 ms.
        var end = Assert.Single(Run(new GestureEngine(), Swipe(0.2, 150, steps: 8)), e => e.Kind == GestureEventKind.SwipeEnd);
        Assert.True(end.Committed);
    }

    [Fact]
    public void VerticalScrollIsIgnored()
    {
        var engine = new GestureEngine();
        var frames = Enumerable.Range(0, 20).Select(i => (Two(0.4, 0.1 + i * 0.02, 0.5, 0.12 + i * 0.02), 1000L + i * 20))
            .Append((Array.Empty<TouchContact>(), 1500L));
        Assert.Empty(Run(engine, frames));
    }

    [Fact]
    public void SwipeDisabledProducesNothing()
    {
        var engine = new GestureEngine { Config = new GestureConfig { SwipeEnabled = false } };
        Assert.Empty(Run(engine, Swipe(0.4, 600)));
    }

    [Fact]
    public void ThreeFingerSwipeNeedsThreeFingerSetting()
    {
        IEnumerable<(TouchContact[], long)> ThreeFingers()
        {
            for (int i = 0; i <= 20; i++)
            {
                double x = 0.3 + 0.4 * i / 20;
                yield return (new[] { new TouchContact(1, x, 0.3), new TouchContact(2, x + 0.1, 0.3), new TouchContact(3, x + 0.2, 0.31) }, 1000 + 30 * i);
            }
            yield return (Array.Empty<TouchContact>(), 1700);
        }

        Assert.Empty(Run(new GestureEngine(), ThreeFingers()));
        var engine = new GestureEngine { Config = new GestureConfig { SwipeFingers = 3 } };
        Assert.Single(Run(engine, ThreeFingers()), e => e.Kind == GestureEventKind.SwipeEnd && e.Committed);
    }

    [Fact]
    public void SpreadingFingersZoomsIn()
    {
        var engine = new GestureEngine();
        var frames = Enumerable.Range(0, 21).Select(i =>
        {
            double half = 0.05 + i * 0.01; // distance grows from 0.1 to 0.5
            return (Two(0.5 - half, 0.3, 0.5 + half, 0.3), 1000L + i * 20);
        });
        var zooms = Run(engine, frames).Where(e => e.Kind == GestureEventKind.ZoomStep).ToList();
        Assert.NotEmpty(zooms);
        Assert.All(zooms, z => Assert.Equal(1, z.Direction));
        Assert.DoesNotContain(Run(engine, new[] { (Array.Empty<TouchContact>(), 2000L) }), e => e.Kind == GestureEventKind.SwipeEnd);
    }

    [Fact]
    public void PinchingFingersZoomsOut()
    {
        var engine = new GestureEngine();
        var frames = Enumerable.Range(0, 21).Select(i =>
        {
            double half = 0.25 - i * 0.01;
            return (Two(0.5 - half, 0.3, 0.5 + half, 0.3), 1000L + i * 20);
        });
        var zooms = Run(engine, frames).Where(e => e.Kind == GestureEventKind.ZoomStep).ToList();
        Assert.NotEmpty(zooms);
        Assert.All(zooms, z => Assert.Equal(-1, z.Direction));
    }

    [Fact]
    public void ResetEndsASwipeWhenFramesStop()
    {
        var engine = new GestureEngine();
        var frames = Swipe(0.4, 600).Where(f => f.Item1.Length > 0).ToList();
        Run(engine, frames);
        var end = Assert.Single(engine.Reset(2000), e => e.Kind == GestureEventKind.SwipeEnd);
        Assert.True(end.Committed);
    }

    [Fact]
    public void HigherSensitivityMeansShorterSwipe()
    {
        Assert.True(new GestureConfig { SwipeSensitivity = 90 }.SwipeDistance < new GestureConfig { SwipeSensitivity = 10 }.SwipeDistance);
        Assert.True(new GestureConfig { PinchSensitivity = 90 }.PinchStepRatio < new GestureConfig { PinchSensitivity = 10 }.PinchStepRatio);
    }
}
