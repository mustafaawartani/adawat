using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Adawat.UI;

/// <summary>The app icon, loaded from the embedded .ico file.</summary>
public static class AppIcon
{
    private static ImageSource? _image;
    private static ImageSource? _translateButton;

    private static Stream Open() =>
        typeof(AppIcon).Assembly.GetManifestResourceStream("Adawat.ico")
        ?? throw new InvalidOperationException("Icon resource missing");

    public static ImageSource ImageSource
    {
        get
        {
            if (_image != null) return _image;
            using var stream = Open();
            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapFrame best = decoder.Frames[0];
            foreach (var frame in decoder.Frames)
            {
                if (frame.PixelWidth > best.PixelWidth) best = frame;
            }
            best.Freeze();
            return _image = best;
        }
    }

    /// <summary>The glass badge of the translate button that appears next to selected text.</summary>
    public static ImageSource TranslateButton
    {
        get
        {
            if (_translateButton != null) return _translateButton;
            using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("Adawat.TranslateButton.png")
                               ?? throw new InvalidOperationException("Translate button resource missing");
            var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            image.Freeze();
            return _translateButton = image;
        }
    }

    /// <summary>A WinForms icon at the given size (for the tray).</summary>
    public static System.Drawing.Icon TrayIcon(int size)
    {
        using var stream = Open();
        return new System.Drawing.Icon(stream, size, size);
    }
}
