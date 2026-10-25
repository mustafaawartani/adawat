using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Adawat.Core;
using Adawat.Interop;
using Adawat.Translator;

namespace Adawat.Ocr;

/// <summary>The "text from screen / image" tool: pick an area or a file, read it, then copy or translate.</summary>
public sealed class OcrTool
{
    private readonly AppSettings _settings;
    private readonly OcrService _ocr;
    private readonly TranslatorService _translator;
    private bool _busy;

    public OcrTool(AppSettings settings, TranslatorService translator)
    {
        _settings = settings;
        _translator = translator;
        _ocr = new OcrService(settings.Ocr);
    }

    public void CaptureScreen()
    {
        if (!_settings.Ocr.Enabled || _busy) return;
        InputSender.MaskMenuKey();
        _ = CaptureScreenAsync();
    }

    private async Task CaptureScreenAsync()
    {
        _busy = true;
        try
        {
            _translator.HideIcon();
            await Task.Delay(120); // let menus and our own popups disappear first
            using var picked = await SnipWindow.PickAsync();
            if (picked == null) return;
            var text = await _ocr.RecognizeAsync(picked);
            Present(text, WindowInfo.CursorPosition(), _settings.Ocr.TranslateAfterCapture);
        }
        catch (Exception ex)
        {
            Log.Error("Screen text capture failed", ex);
            ShowError(ex);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Reads a picture file (from the Explorer right-click menu).</summary>
    public async Task ReadFileAsync(string path, bool translate)
    {
        try
        {
            var text = await _ocr.RecognizeFileAsync(path);
            Present(text, WindowInfo.CursorPosition(), translate || _settings.Ocr.TranslateAfterCapture);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not read text from {path}", ex);
            ShowError(ex);
        }
    }

    private void Present(string text, Native.POINT anchor, bool translate)
    {
        if (_settings.Ocr.CopyAfterCapture && text.Length > 0)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    break;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    Thread.Sleep(30);
                }
            }
        }
        _translator.TranslateText(text, PopupMode.Image, anchor, translate && text.Length > 0);
    }

    private void ShowError(Exception ex)
    {
        var message = ex is InvalidOperationException ? ex.Message : "Adawat could not read text from this picture.";
        _translator.TranslateText("", PopupMode.Image, WindowInfo.CursorPosition(), false, message);
    }
}
