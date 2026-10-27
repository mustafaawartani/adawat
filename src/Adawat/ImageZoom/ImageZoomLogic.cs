using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
