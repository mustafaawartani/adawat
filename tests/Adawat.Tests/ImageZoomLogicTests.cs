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

public class FullSizePictureTests
{
    [Theory]
    [InlineData("https://yt3.ggpht.com/ytc/AIdro_abc123=s88-c-k-c0x00ffffff-no-rj", "https://yt3.ggpht.com/ytc/AIdro_abc123=s0")]
    [InlineData("https://lh3.googleusercontent.com/a/ACg8oc=s96-c", "https://lh3.googleusercontent.com/a/ACg8oc=s0")]
    [InlineData("https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg?sqp=-oaymwE&rs=AOn4", "https://i.ytimg.com/vi/dQw4w9WgXcQ/maxresdefault.jpg")]
    [InlineData("https://i.ytimg.com/vi_webp/dQw4w9WgXcQ/mqdefault.webp", "https://i.ytimg.com/vi/dQw4w9WgXcQ/maxresdefault.jpg")]
    [InlineData("https://pbs.twimg.com/media/GabcXYZ?format=jpg&name=small", "https://pbs.twimg.com/media/GabcXYZ?format=jpg&name=orig")]
    [InlineData("https://pbs.twimg.com/profile_images/123/photo_normal.jpg", "https://pbs.twimg.com/profile_images/123/photo.jpg")]
    [InlineData("https://upload.wikimedia.org/wikipedia/commons/thumb/a/ab/Cat.jpg/220px-Cat.jpg", "https://upload.wikimedia.org/wikipedia/commons/a/ab/Cat.jpg")]
    [InlineData("https://preview.redd.it/abc123.jpg?width=640&auto=webp", "https://i.redd.it/abc123.jpg")]
    [InlineData("https://m.media-amazon.com/images/I/71abcDEF._AC_SX300_.jpg", "https://m.media-amazon.com/images/I/71abcDEF.jpg")]
    [InlineData("https://i.imgur.com/AbCdEfGm.jpg", "https://i.imgur.com/AbCdEfG.jpg")]
    [InlineData("https://i.pinimg.com/236x/aa/bb/cc/photo.jpg", "https://i.pinimg.com/originals/aa/bb/cc/photo.jpg")]
    [InlineData("https://example.com/wp-content/uploads/2024/05/photo-300x200.jpg", "https://example.com/wp-content/uploads/2024/05/photo.jpg")]
    [InlineData("https://images.example.com/photo.jpg?w=300&h=200&auto=format", "https://images.example.com/photo.jpg?auto=format")]
    public void TriesTheFullSizePictureFirst(string shown, string full)
    {
        var candidates = ImageZoomLogic.FullSizeCandidates(shown);
        Assert.Equal(full, candidates[0]);
        Assert.Equal(shown, candidates[^1]); // the picture as shown is always the last resort
    }

    [Fact]
    public void PlainAddressesAreTriedAsTheyAre()
    {
        Assert.Equal(new[] { "https://example.com/a.png" }, ImageZoomLogic.FullSizeCandidates("https://example.com/a.png"));
        Assert.Equal(new[] { "data:image/png;base64,AA" }, ImageZoomLogic.FullSizeCandidates("data:image/png;base64,AA"));
    }

    [Theory]
    [InlineData("https://yt3.ggpht.com/abc=s88-c-k", true)]  // no extension, still a picture
    [InlineData("data:image/png;base64,AA", true)]
    [InlineData("javascript:void(0)", false)]
    [InlineData("", false)]
    public void PictureSourcesNeedNoExtension(string url, bool expected) =>
        Assert.Equal(expected, ImageZoomLogic.IsPictureSource(url));
}

public class PreviewScaleTests
{
    [Fact]
    public void BigPicturesAreShownAtRealSize()
    {
        Assert.Equal(1, ImageZoomLogic.MaxPreviewScale(3000, 2000, 300, 200, 560), 6);
    }

    [Fact]
    public void SmallPicturesAreEnlargedWellPastTheirSizeOnScreen()
    {
        // A 240×135 thumbnail shown at 240×135: the preview must be clearly bigger, not the same small picture.
        double scale = ImageZoomLogic.MaxPreviewScale(240, 135, 240, 135, 560);
        Assert.True(240 * scale >= 560, $"scale {scale}");
    }

    [Fact]
    public void EnlargingHasALimit()
    {
        Assert.Equal(ImageZoomLogic.MaxUpscale, ImageZoomLogic.MaxPreviewScale(40, 40, 40, 40, 560), 6);
    }

    [Fact]
    public void ScreenCopiesOfSmallPicturesOpenBig()
    {
        double scale = ImageZoomLogic.MaxPreviewScale(320, 180, 320, 180, 560);
        var layout = ImageZoomLogic.Place(320, 180, scale, new PixelRect(0, 0, 1920, 1040), 400, 500, (40, 70), 12);
        Assert.True(layout.Window.Width > 320 * 2, $"window {layout.Window.Width}");
    }
}
