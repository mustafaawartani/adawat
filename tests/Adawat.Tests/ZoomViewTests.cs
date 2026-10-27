using Adawat.Gestures;
using Xunit;

namespace Adawat.Tests;

public class ZoomViewTests
{
    private static ZoomView NewView() => new() { ScreenWidth = 1920, ScreenHeight = 1080, MaxLevel = 5 };

    /// <summary>Runs frames of 1/60 s until settled (or a time limit).</summary>
    private static int Settle(ZoomView view, double seconds = 3)
    {
        int frames = 0;
        for (; frames < seconds * 60 && !view.IsSettled; frames++) view.Step(1 / 60.0);
        view.Step(1 / 60.0);
        return frames;
    }

    [Fact]
    public void ZoomGlidesToTheTargetInsteadOfJumping()
    {
        var view = NewView();
        view.Pinching = true;
        view.AnchorAt(960, 540);
        view.ZoomTo(3);
        view.Step(1 / 60.0);
        Assert.InRange(view.Level, 1.01, 2.9);
        Settle(view);
        Assert.Equal(3, view.Level, 3);
    }

    [Fact]
    public void AnchorStaysInPlaceAtEveryInBetweenLevel()
    {
        var view = NewView();
        view.Pinching = true;
        view.AnchorAt(600, 300);
        view.ZoomTo(4);
        for (int i = 0; i < 40; i++)
        {
            view.Step(1 / 60.0);
            var (x, y) = view.Shown(600, 300);
            Assert.Equal(600, x, 6);
            Assert.Equal(300, y, 6);
        }
    }

    [Fact]
    public void NoisyFingersMoveTheZoomLessThanTheNoise()
    {
        var view = NewView();
        view.Pinching = true;
        view.AnchorAt(960, 540);
        view.ZoomTo(2);
        Settle(view);
        // Fingers jitter ±5% around 2x every frame.
        double minLevel = double.MaxValue, maxLevel = double.MinValue;
        for (int i = 0; i < 60; i++)
        {
            view.ZoomTo(i % 2 == 0 ? 2.1 : 1.9);
            view.Step(1 / 120.0);
            if (i > 10)
            {
                minLevel = Math.Min(minLevel, view.Level);
                maxLevel = Math.Max(maxLevel, view.Level);
            }
        }
        Assert.True(maxLevel - minLevel < 0.2 * 0.5, $"Shown level swung {maxLevel - minLevel:0.000}");
    }

    [Fact]
    public void ViewNeverLeavesTheScreen()
    {
        var view = NewView();
        view.AnchorAt(1900, 1070);
        view.ZoomTo(5);
        Settle(view);
        Assert.InRange(view.X, 0, 1920 - 1920 / 5.0);
        Assert.InRange(view.Y, 0, 1080 - 1080 / 5.0);
        view.PanBy(5000, 5000);
        Settle(view);
        Assert.Equal(1920 - 1920 / 5.0, view.X, 3);
        Assert.Equal(1080 - 1080 / 5.0, view.Y, 3);
    }

    [Theory]
    [InlineData(2.2, 2)]
    [InlineData(2.8, 3)]
    [InlineData(2.5, 2.5)]
    [InlineData(1.4, 1.4)]
    public void SnapsToWholeLevelsOnlyWhenClose(double level, double expected)
    {
        var view = NewView();
        view.AnchorAt(960, 540);
        view.ZoomTo(level);
        view.SnapToWholeLevel();
        Assert.Equal(expected, view.TargetLevel, 6);
    }

    [Fact]
    public void ZoomingOutReturnsToNormal()
    {
        var view = NewView();
        view.AnchorAt(300, 300);
        view.ZoomTo(3);
        Settle(view);
        Assert.True(view.IsZoomed);
        view.ZoomTo(1);
        Settle(view);
        Assert.False(view.IsZoomed);
        Assert.Equal(0, view.X, 6);
        Assert.Equal(0, view.Y, 6);
    }
}
