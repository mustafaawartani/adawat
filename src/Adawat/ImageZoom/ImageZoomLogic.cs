using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Adawat.ImageZoom;

/// <summary>A rectangle in physical screen pixels.</summary>
public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;

    public bool Contains(int x, int y, int slack = 0) =>
        x >= Left - slack && x < Right + slack && y >= Top - slack && y < Bottom + slack;
}

/// <summary>The size and place of the preview window and the picture inside it.</summary>
public readonly record struct PreviewLayout(PixelRect Window, double Scale);

/// <summary>Decisions of the picture zoom that do not need Windows, kept apart so they can be tested.</summary>
public static class ImageZoomLogic
{
    public static readonly IReadOnlyCollection<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jfif", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico", ".heic", ".heif", ".avif", ".dib",
    };

    public static bool IsImageFile(string? path) =>
        !string.IsNullOrEmpty(path) && ImageExtensions.Contains(Path.GetExtension(path));

    /// <summary>True for links that point straight at a picture (http(s) with an image extension, or a data: image).</summary>
    public static bool LooksLikeImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return true;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return false;
        if (IsImageFile(uri.AbsolutePath)) return true;
        // CDNs often put the format in the query: ?format=jpg, &fm=webp
        var query = uri.Query.ToLowerInvariant();
        return new[] { "format=jpg", "format=jpeg", "format=png", "format=webp", "fm=jpg", "fm=png", "fm=webp" }.Any(query.Contains);
    }

    /// <summary>The address of a picture element itself (its src): any web or data address counts, with or without an extension.</summary>
    public static bool IsPictureSource(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return true;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>Picture servers whose addresses often have no extension (YouTube, Google, Twitter…).</summary>
    public static bool IsPictureHost(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.ToLowerInvariant();
        return host.EndsWith("ggpht.com") || host.EndsWith("googleusercontent.com") || host.EndsWith("ytimg.com")
               || host == "pbs.twimg.com" || host == "i.redd.it" || host == "preview.redd.it" || host == "i.imgur.com"
               || host == "upload.wikimedia.org" || host.EndsWith("pinimg.com") || host.EndsWith("media-amazon.com");
    }

    private static readonly Regex GoogleSize = new(@"=([swhc][0-9]+[^/=?#]*)$", RegexOptions.IgnoreCase);
    private static readonly Regex GoogleSizeSegment = new(@"/s[0-9]+(-[a-z0-9-]+)?/", RegexOptions.IgnoreCase);
    private static readonly Regex YouTubeThumb = new(@"^/vi(_webp)?/([^/]+)/[^/]+\.(jpg|webp)$", RegexOptions.IgnoreCase);
    private static readonly Regex TwitterProfile = new(@"_(normal|bigger|mini|200x200|400x400)(\.[a-z]+)$", RegexOptions.IgnoreCase);
    private static readonly Regex WikimediaThumb = new(@"^(/wikipedia/[^/]+)/thumb(/[0-9a-f]/[0-9a-f]{2}/[^/]+)/[^/]+$", RegexOptions.IgnoreCase);
    private static readonly Regex AmazonSize = new(@"\._[^/]+_(\.[a-z]+)$", RegexOptions.IgnoreCase);
    private static readonly Regex ImgurSuffix = new(@"^/([a-zA-Z0-9]{7})[sbtmlh](\.[a-z]+)$");
    private static readonly Regex WordPressSize = new(@"-[0-9]{2,4}x[0-9]{2,4}(\.[a-z]+)$", RegexOptions.IgnoreCase);
    private static readonly string[] SizeQueryKeys = { "w", "h", "width", "height", "size", "resize", "fit", "crop", "quality", "q", "dpr", "imwidth" };

    /// <summary>
    /// Pages show small versions of pictures. These are addresses to try for the full-size picture,
    /// best first, always ending with the address itself: YouTube and Google profile pictures and
    /// thumbnails, Twitter/X, Wikipedia, Reddit, Imgur, Amazon, Pinterest, WordPress and image CDNs
    /// that take the size in the query.
    /// </summary>
    public static IReadOnlyList<string> FullSizeCandidates(string url)
    {
        var list = new List<string>();
        void Add(string candidate)
        {
            if (!string.IsNullOrEmpty(candidate) && !list.Contains(candidate)) list.Add(candidate);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            Add(url);
            return list;
        }
        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath;
        string Origin() => uri.GetLeftPart(UriPartial.Authority);

        if (host.EndsWith("ggpht.com") || host.EndsWith("googleusercontent.com"))
        {
            // https://yt3.ggpht.com/abc=s88-c-k-c0x00ffffff-no-rj  ->  =s0 is the picture as uploaded
            var full = uri.GetLeftPart(UriPartial.Path);
            if (GoogleSize.IsMatch(full))
            {
                Add(GoogleSize.Replace(full, "=s0"));
                Add(GoogleSize.Replace(full, "=s1600"));
            }
            else if (GoogleSizeSegment.IsMatch(path))
            {
                Add(Origin() + GoogleSizeSegment.Replace(path, "/s0/", 1));
            }
        }
        else if (host.EndsWith("ytimg.com") && YouTubeThumb.Match(path) is { Success: true } thumb)
        {
            var id = thumb.Groups[2].Value;
            foreach (var name in new[] { "maxresdefault.jpg", "sddefault.jpg", "hqdefault.jpg" }) Add($"https://i.ytimg.com/vi/{id}/{name}");
        }
        else if (host == "pbs.twimg.com")
        {
            if (path.StartsWith("/media/", StringComparison.OrdinalIgnoreCase))
            {
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var format = query["format"] ?? (Path.HasExtension(path) ? Path.GetExtension(path).TrimStart('.') : "jpg");
                var stem = Path.HasExtension(path) ? path[..^Path.GetExtension(path).Length] : path;
                Add($"{Origin()}{stem}?format={format}&name=orig");
                Add($"{Origin()}{stem}?format={format}&name=large");
            }
            else if (TwitterProfile.IsMatch(path))
            {
                Add(Origin() + TwitterProfile.Replace(path, "$2"));
                Add(Origin() + TwitterProfile.Replace(path, "_400x400$2"));
            }
        }
        else if (host == "upload.wikimedia.org" && WikimediaThumb.Match(path) is { Success: true } wiki)
        {
            Add(Origin() + wiki.Groups[1].Value + wiki.Groups[2].Value);
        }
        else if (host == "preview.redd.it")
        {
            Add("https://i.redd.it" + path);
        }
        else if (host == "i.imgur.com" && ImgurSuffix.Match(path) is { Success: true } imgur)
        {
            Add($"{Origin()}/{imgur.Groups[1].Value}{imgur.Groups[2].Value}");
        }
        else if (host.EndsWith("media-amazon.com") && AmazonSize.IsMatch(path))
        {
            Add(Origin() + AmazonSize.Replace(path, "$1"));
        }
        else if (host.EndsWith("pinimg.com") && Regex.IsMatch(path, @"^/[0-9]+x[0-9]*/"))
        {
            Add(Origin() + Regex.Replace(path, @"^/[0-9]+x[0-9]*/", "/originals/"));
        }
        else
        {
            if (WordPressSize.IsMatch(path)) Add(Origin() + WordPressSize.Replace(path, "$1") + uri.Query);
            // Image CDNs that resize on request: ?w=300&h=200 -> the same picture without the size.
            if (uri.Query.Length > 1)
            {
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                bool sized = false;
                foreach (var key in SizeQueryKeys)
                {
                    if (query[key] == null) continue;
                    query.Remove(key);
                    sized = true;
                }
                if (sized)
                {
                    var rest = query.ToString();
                    Add(uri.GetLeftPart(UriPartial.Path) + (string.IsNullOrEmpty(rest) ? "" : "?" + rest));
                }
            }
        }
        Add(url);
        return list;
    }

    /// <summary>A file name for saving: the name in the link or file, else "image-date-time.png".</summary>
    public static string SuggestedFileName(string? source, string fallbackExtension, DateTime now)
    {
        string? name = null;
        if (!string.IsNullOrEmpty(source) && !source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            name = Uri.TryCreate(source, UriKind.Absolute, out var uri) && !uri.IsFile
                ? Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath))
                : Path.GetFileName(source);
        }
        if (!string.IsNullOrWhiteSpace(name))
        {
            foreach (var c in Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' })) name = name.Replace(c, '_');
            if (!Path.HasExtension(name)) name += fallbackExtension;
            if (name.Length > 120) name = name[..(120 - Path.GetExtension(name).Length)] + Path.GetExtension(name);
            return name;
        }
        return $"image-{now:yyyyMMdd-HHmmss}{fallbackExtension}";
    }

    /// <summary>"photo.jpg", "photo (2).jpg"… the first name not in <paramref name="exists"/>.</summary>
    public static string UniqueName(string fileName, Func<string, bool> exists)
    {
        if (!exists(fileName)) return fileName;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (int i = 2; i < 10000; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (!exists(candidate)) return candidate;
        }
        return $"{stem}-{Guid.NewGuid():N}{ext}";
    }

    /// <summary>
    /// Finds a file shown as <paramref name="displayName"/> in a folder listing. File Explorer hides
    /// known extensions, so "Holiday" can mean "Holiday.jpg". Only picture files are returned.
    /// </summary>
    public static string? MatchImageFile(string displayName, IEnumerable<string> fileNames)
    {
        displayName = CleanDisplayName(displayName);
        if (displayName.Length == 0) return null;
        var names = fileNames.ToList();
        var exact = names.FirstOrDefault(n => string.Equals(n, displayName, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return IsImageFile(exact) ? exact : null;
        return names.FirstOrDefault(n => IsImageFile(n) && string.Equals(Path.GetFileNameWithoutExtension(n), displayName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// File Explorer adds invisible direction marks to some names (for example with Arabic or Hebrew
    /// in Windows' languages), so "\u200Ephoto" must still find "photo.jpg".
    /// </summary>
    public static string CleanDisplayName(string name)
    {
        var chars = name.Where(c => c is not ('\u200E' or '\u200F' or '\u061C' or >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069' or '\uFEFF'));
        return new string(chars.ToArray()).Trim();
    }

    /// <summary>
    /// Places the preview of a <paramref name="imageWidth"/>×<paramref name="imageHeight"/> picture next to
    /// the pointer, on the side with the most room, as big as possible but never above
    /// <paramref name="maxScale"/> (1 = real size). <paramref name="chrome"/> is the frame and caption
    /// size around the picture, <paramref name="margin"/> the distance kept from the screen edge and the pointer.
    /// </summary>
    public static PreviewLayout Place(int imageWidth, int imageHeight, double maxScale, PixelRect work, int pointerX, int pointerY,
        (int Width, int Height) chrome, int margin)
    {
        imageWidth = Math.Max(1, imageWidth);
        imageHeight = Math.Max(1, imageHeight);
        double FitIn(int width, int height) => Math.Min(maxScale, Math.Min(
            Math.Max(0, width - chrome.Width) / (double)imageWidth,
            Math.Max(0, height - chrome.Height) / (double)imageHeight));

        int height = work.Height - 2 * margin;
        int rightWidth = work.Right - margin - (pointerX + margin);
        int leftWidth = pointerX - margin - (work.Left + margin);
        double right = FitIn(rightWidth, height);
        double left = FitIn(leftWidth, height);
        double full = FitIn(work.Width - 2 * margin, height);

        // Beside the pointer keeps what is under it visible; use the whole screen only if that is much bigger.
        double best = Math.Max(left, right);
        string where = best >= full * 0.85 ? (right >= left ? "right" : "left") : "full";
        double scale = where == "full" ? full : best;
        scale = Math.Max(scale, 0.01);

        int w = (int)Math.Round(imageWidth * scale) + chrome.Width;
        int h = (int)Math.Round(imageHeight * scale) + chrome.Height;
        int x = where switch
        {
            "right" => pointerX + margin,
            "left" => pointerX - margin - w,
            _ => pointerX - w / 2,
        };
        int y = pointerY - h / 2;
        x = Math.Clamp(x, work.Left + margin, Math.Max(work.Left + margin, work.Right - margin - w));
        y = Math.Clamp(y, work.Top + margin, Math.Max(work.Top + margin, work.Bottom - margin - h));
        return new PreviewLayout(new PixelRect(x, y, w, h), scale);
    }
}
