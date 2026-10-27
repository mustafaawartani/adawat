using Adawat.ImageZoom;
using Xunit;

namespace Adawat.Tests;

public class ImageZoomLogicTests
{
    private static readonly PixelRect Work = new(0, 0, 1920, 1040);

    [Theory]
    [InlineData("https://example.com/photos/cat.JPG", true)]
    [InlineData("https://cdn.example.com/img/123?format=webp&w=200", true)]
    [InlineData("data:image/png;base64,AAAA", true)]
    [InlineData("https://example.com/article/cats", false)]
    [InlineData("javascript:void(0)", false)]
    [InlineData("", false)]
    public void RecognizesLinksToPictures(string url, bool expected) =>
        Assert.Equal(expected, ImageZoomLogic.LooksLikeImageUrl(url));

    [Fact]
    public void FindsPictureFilesWhoseExtensionExplorerHides()
    {
        var files = new[] { "Holiday.txt", "Holiday.jpg", "Other.png" };
        Assert.Equal("Holiday.jpg", ImageZoomLogic.MatchImageFile("Holiday", files));
        Assert.Equal("Other.png", ImageZoomLogic.MatchImageFile("Other.png", files));
        Assert.Null(ImageZoomLogic.MatchImageFile("Holiday.txt", files));
        Assert.Null(ImageZoomLogic.MatchImageFile("Missing", files));
    }

    [Fact]
    public void IgnoresInvisibleDirectionMarksInExplorerNames()
    {
        var files = new[] { "صورة.png", "Holiday.jpg" };
        Assert.Equal("Holiday.jpg", ImageZoomLogic.MatchImageFile("\u200EHoliday\u200E", files));
        Assert.Equal("صورة.png", ImageZoomLogic.MatchImageFile("\u200Fصورة\u200F", files));
    }

    [Fact]
    public void SaveNamesComeFromTheSourceAndNeverOverwrite()
    {
        var now = new DateTime(2026, 10, 27, 9, 30, 5);
        Assert.Equal("my cat.jpg", ImageZoomLogic.SuggestedFileName("https://x.com/a/my%20cat.jpg?size=large", ".png", now));
        Assert.Equal("image-20261027-093005.png", ImageZoomLogic.SuggestedFileName(null, ".png", now));
        Assert.Equal("image-20261027-093005.png", ImageZoomLogic.SuggestedFileName("data:image/png;base64,AA", ".png", now));
        var taken = new HashSet<string> { "a.jpg", "a (2).jpg" };
        Assert.Equal("a (3).jpg", ImageZoomLogic.UniqueName("a.jpg", taken.Contains));
    }

    [Fact]
    public void SmallPictureIsShownAtRealSizeBesideThePointer()
    {
        var layout = ImageZoomLogic.Place(400, 300, 1, Work, 300, 500, (40, 70), 12);
        Assert.Equal(1, layout.Scale, 6);
        Assert.Equal(440, layout.Window.Width);
        Assert.Equal(370, layout.Window.Height);
        Assert.True(layout.Window.Left > 300, "should open on the right, where there is more room");
    }

    [Fact]
    public void BigPictureIsScaledDownToFitTheScreen()
    {
        var layout = ImageZoomLogic.Place(6000, 4000, 1, Work, 960, 520, (40, 70), 12);
        Assert.True(layout.Scale < 1);
        Assert.True(layout.Window.Left >= Work.Left && layout.Window.Right <= Work.Right);
        Assert.True(layout.Window.Top >= Work.Top && layout.Window.Bottom <= Work.Bottom);
    }

    [Fact]
    public void PreviewOpensOnTheSideWithMoreRoom()
    {
        var layout = ImageZoomLogic.Place(800, 600, 1, Work, 1700, 500, (40, 70), 12);
        Assert.True(layout.Window.Right < 1700);
    }

    [Fact]
    public void ScreenCapturesMayBeEnlargedUpToTheLimit()
    {
        var layout = ImageZoomLogic.Place(100, 80, 3, Work, 200, 500, (40, 70), 12);
        Assert.Equal(3, layout.Scale, 6);
    }
}
