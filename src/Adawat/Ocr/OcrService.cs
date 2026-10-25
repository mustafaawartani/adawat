using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Adawat.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Adawat.Ocr;

public sealed record OcrLanguageOption(string Tag, string Name);

/// <summary>Reads text from pictures with the OCR engine built into Windows (offline, free).</summary>
public sealed class OcrService
{
    private readonly OcrSettings _settings;

    public OcrService(OcrSettings settings) => _settings = settings;

    public static IReadOnlyList<OcrLanguageOption> InstalledLanguages()
    {
        try
        {
            return OcrEngine.AvailableRecognizerLanguages
                .Select(l => new OcrLanguageOption(l.LanguageTag, l.DisplayName))
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error("Could not list OCR languages", ex);
            return Array.Empty<OcrLanguageOption>();
        }
    }

    /// <summary>Recognizes text in an encoded picture (PNG, JPG, BMP, GIF, TIFF, WebP...).</summary>
    public async Task<string> RecognizeAsync(IRandomAccessStream stream)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        uint width = decoder.OrientedPixelWidth, height = decoder.OrientedPixelHeight;
        if (width == 0 || height == 0) return "";

        // Small text reads much better when enlarged; very large pictures must be reduced.
        double max = OcrEngine.MaxImageDimension;
        double scale = Math.Min(height < 600 && width < 1600 ? 2.0 : 1.0, Math.Min(max / width, max / height));
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, width * scale),
            ScaledHeight = (uint)Math.Max(1, height * scale),
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);

        var engines = CreateEngines();
        if (engines.Count == 0)
            throw new InvalidOperationException("No OCR language is installed in Windows. Add a language in Settings → Time & language.");

        string best = "";
        foreach (var engine in engines)
        {
            var result = await engine.RecognizeAsync(bitmap);
            var text = Format(result);
            if (CountLetters(text) > CountLetters(best)) best = text;
            if (_settings.Language != "auto") break;
        }
        return best;
    }

    public async Task<string> RecognizeFileAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        using var stream = await file.OpenAsync(FileAccessMode.Read);
        return await RecognizeAsync(stream);
    }

    public async Task<string> RecognizeAsync(System.Drawing.Bitmap image)
    {
        using var ms = new MemoryStream();
        image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        using var stream = ms.AsRandomAccessStream();
        return await RecognizeAsync(stream);
    }

    /// <summary>
    /// The chosen language, or in automatic mode the Windows display languages first, then any
    /// other installed OCR language (the result with the most letters wins).
    /// </summary>
    private List<OcrEngine> CreateEngines()
    {
        var engines = new List<OcrEngine>();
        if (_settings.Language != "auto")
        {
            try
            {
                var language = new Language(_settings.Language);
                if (OcrEngine.IsLanguageSupported(language))
                {
                    var engine = OcrEngine.TryCreateFromLanguage(language);
                    if (engine != null) engines.Add(engine);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"OCR language {_settings.Language} is not available", ex);
            }
        }

        if (engines.Count == 0)
        {
            var profile = OcrEngine.TryCreateFromUserProfileLanguages();
            if (profile != null) engines.Add(profile);
            foreach (var language in OcrEngine.AvailableRecognizerLanguages)
            {
                if (profile != null && profile.RecognizerLanguage.LanguageTag == language.LanguageTag) continue;
                var engine = OcrEngine.TryCreateFromLanguage(language);
                if (engine != null && engines.Count < 4) engines.Add(engine);
            }
        }
        return engines;
    }

    private string Format(OcrResult result)
    {
        var sb = new StringBuilder();
        foreach (var line in result.Lines)
        {
            if (sb.Length > 0) sb.Append(_settings.KeepLineBreaks ? "\n" : " ");
            sb.Append(line.Text);
        }
        return sb.ToString().Trim();
    }

    private static int CountLetters(string text) => text.Count(char.IsLetterOrDigit);
}
