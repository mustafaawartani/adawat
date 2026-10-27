using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Adawat.Core;
using Drawing = System.Drawing;

namespace Adawat.ImageZoom;

/// <summary>A picture ready to show, with what is needed to save or copy it at full quality.</summary>
public sealed class LoadedImage
{
    public required BitmapSource Picture { get; init; }
    public required int PixelWidth { get; init; }
    public required int PixelHeight { get; init; }
    /// <summary>The original file bytes (saved as they are), or null for a screen capture.</summary>
    public byte[]? OriginalBytes { get; init; }
    public required string FileName { get; init; }
    /// <summary>True when only what is on screen could be shown (no file or address for the full picture).</summary>
    public bool IsScreenCapture { get; init; }
}

/// <summary>Loads the full picture from a file or the web, or captures it from the screen.</summary>
public static class ImageLoader
{
    private const int MaxBytes = 40 * 1024 * 1024;
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/png,image/*;q=0.8,*/*;q=0.5");
        return client;
    }

    public static async Task<LoadedImage?> LoadAsync(ImageHit hit, CancellationToken cancel)
    {
        try
        {
            if (hit.FilePath != null)
            {
                var bytes = await File.ReadAllBytesAsync(hit.FilePath, cancel);
                return await Task.Run(() => Decode(bytes, Path.GetFileName(hit.FilePath)), cancel);
            }
            if (hit.Url != null)
            {
                var bytes = await DownloadAsync(hit.Url, cancel);
                if (bytes != null)
                {
                    var name = ImageZoomLogic.SuggestedFileName(hit.Url, ".png", DateTime.Now);
                    var image = await Task.Run(() => Decode(bytes, name), cancel);
                    if (image != null) return image;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Info($"Picture zoom could not load the full picture: {ex.Message}");
        }
        if (cancel.IsCancellationRequested || hit.FilePath != null) return null;
        return Capture(hit.Bounds);
    }

    private static async Task<byte[]?> DownloadAsync(string url, CancellationToken cancel)
    {
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int comma = url.IndexOf(',');
            if (comma < 0 || !url[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) return null;
            return Convert.FromBase64String(url[(comma + 1)..]);
        }
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (!response.IsSuccessStatusCode) return null;
        if (response.Content.Headers.ContentLength > MaxBytes) return null;
        var bytes = await response.Content.ReadAsByteArrayAsync(cancel);
        return bytes.Length > MaxBytes ? null : bytes;
    }

    /// <summary>Decodes a picture (any format Windows can read), turned upright as the camera recorded it.</summary>
    private static LoadedImage? Decode(byte[] bytes, string fileName)
    {
        try
        {
            var frame = BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource picture = frame;
            int rotation = Orientation(frame) switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
            if (rotation != 0) picture = new TransformedBitmap(frame, new RotateTransform(rotation));
            picture.Freeze();
            return new LoadedImage
            {
                Picture = picture,
                PixelWidth = picture.PixelWidth,
                PixelHeight = picture.PixelHeight,
                OriginalBytes = bytes,
                FileName = fileName,
            };
        }
        catch (Exception ex)
        {
            Log.Info($"Picture zoom could not decode {fileName}: {ex.Message}");
            return null;
        }
    }

    private static int Orientation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata && metadata.ContainsQuery("System.Photo.Orientation")
                && metadata.GetQuery("System.Photo.Orientation") is ushort value)
                return value;
        }
        catch
        {
        }
        return 1;
    }

    /// <summary>What is on screen inside <paramref name="bounds"/> (physical pixels).</summary>
    public static LoadedImage? Capture(PixelRect bounds)
    {
        try
        {
            var area = Drawing.Rectangle.Intersect(new Drawing.Rectangle(bounds.Left, bounds.Top, bounds.Width, bounds.Height),
                System.Windows.Forms.SystemInformation.VirtualScreen);
            if (area.Width < 8 || area.Height < 8) return null;
            using var bitmap = new Drawing.Bitmap(area.Width, area.Height, Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Drawing.Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(area.Left, area.Top, 0, 0, area.Size, Drawing.CopyPixelOperation.SourceCopy);
            }
            var data = bitmap.LockBits(new Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                Drawing.Imaging.ImageLockMode.ReadOnly, Drawing.Imaging.PixelFormat.Format32bppArgb);
            BitmapSource picture;
            try
            {
                picture = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32, null,
                    data.Scan0, data.Stride * bitmap.Height, data.Stride);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            picture.Freeze();
            return new LoadedImage
            {
                Picture = picture,
                PixelWidth = picture.PixelWidth,
                PixelHeight = picture.PixelHeight,
                FileName = ImageZoomLogic.SuggestedFileName(null, ".png", DateTime.Now),
                IsScreenCapture = true,
            };
        }
        catch (Exception ex)
        {
            Log.Info($"Picture zoom could not capture the screen: {ex.Message}");
            return null;
        }
    }

    /// <summary>Saves into the Downloads folder (original bytes when there are some) and returns the file path.</summary>
    public static string Save(LoadedImage image)
    {
        var folder = KnownFolders.Downloads;
        Directory.CreateDirectory(folder);
        var name = ImageZoomLogic.UniqueName(image.FileName, n => File.Exists(Path.Combine(folder, n)));
        var path = Path.Combine(folder, name);
        if (image.OriginalBytes != null)
        {
            File.WriteAllBytes(path, image.OriginalBytes);
        }
        else
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image.Picture));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }
        return path;
    }

    /// <summary>Copies the picture (as a bitmap and as PNG, which keeps transparency in apps that read it).</summary>
    public static void Copy(LoadedImage image)
    {
        var data = new DataObject();
        data.SetImage(image.Picture);
        var png = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image.Picture));
        encoder.Save(png);
        png.Position = 0;
        data.SetData("PNG", png, false);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(30);
            }
        }
        throw new InvalidOperationException("The clipboard is busy.");
    }
}

/// <summary>Windows' known folders that .NET does not name.</summary>
public static class KnownFolders
{
    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out string path);

    public static string Downloads
    {
        get
        {
            var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            try
            {
                if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var path) == 0 && !string.IsNullOrEmpty(path)) return path;
            }
            catch
            {
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }
}
