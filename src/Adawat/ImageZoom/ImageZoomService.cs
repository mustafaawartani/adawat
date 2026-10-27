using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Adawat.Core;
using Adawat.Interop;

namespace Adawat.ImageZoom;

/// <summary>
/// The picture zoom tool, like the Photo Zoom browser extension but in every app: rest the pointer
/// on a picture to see it big, then press a shortcut to save it to Downloads or copy it.
/// The shortcuts are only taken while the preview is open, so they keep working in other apps.
/// </summary>
public sealed class ImageZoomService : IDisposable
{
    private const string SaveHotkeyName = "imagesave";
    private const string CopyHotkeyName = "imagecopy";

    private readonly AppSettings _settings;
    private readonly HotkeyManager _hotkeys;
    private readonly DispatcherTimer _poll;
    private ImageFinder? _finder;
    private ImagePreviewWindow? _window;

    private Native.POINT _lastPos;
    private Native.POINT? _lastLookup;
    private long _restSince;
    private bool _looking;
    private ImageHit? _hit;          // the picture being loaded or shown
    private LoadedImage? _image;     // the picture shown
    private CancellationTokenSource? _loading;
    private int _generation;

    private ImageZoomSettings S => _settings.ImageZoom;

    public ImageZoomService(AppSettings settings, HotkeyManager hotkeys)
    {
        _settings = settings;
        _hotkeys = hotkeys;
        // Only the pointer position is read on each tick, which costs next to nothing.
        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(60) };
        _poll.Tick += (_, _) => Tick();
    }

    public void Apply()
    {
        if (S.Enabled) _poll.Start();
        else
        {
            _poll.Stop();
            Hide();
        }
    }

    private void Tick()
    {
        var p = WindowInfo.CursorPosition();
        long now = Environment.TickCount64;
        if (p.X != _lastPos.X || p.Y != _lastPos.Y)
        {
            _lastPos = p;
            _restSince = now;
        }

        if (_hit != null)
        {
            if (!_hit.Bounds.Contains(p.X, p.Y, slack: 2) || !ModifierHeld() || ButtonDown()) Hide();
            return;
        }
        if (_looking || now - _restSince < Math.Clamp(S.HoverDelayMs, 0, 3000)) return;
        if (_lastLookup is { } last && last.X == p.X && last.Y == p.Y) return;
        if (!ModifierHeld() || ButtonDown()) return;

        var root = WindowInfo.RootWindowAt(p);
        if (root == IntPtr.Zero || WindowInfo.IsOwnWindow(root)) return;
        if (WindowInfo.ListContains(S.DisabledApps, WindowInfo.ProcessName(root))) return;
        if (S.DisableInFullscreen && WindowInfo.IsFullscreenAppRunning()) return;

        _lastLookup = p;
        _ = LookAsync(p);
    }

    private async Task LookAsync(Native.POINT p)
    {
        _looking = true;
        ImageHit? hit;
        try
        {
            _finder ??= new ImageFinder();
            var find = _finder.FindAsync(p, Math.Max(16, S.MinSize));
            hit = await Task.WhenAny(find, Task.Delay(1500)) == find ? find.Result : null;
        }
        finally
        {
            _looking = false;
        }

        var cursor = WindowInfo.CursorPosition();
        if (hit == null || _hit != null || !hit.Bounds.Contains(cursor.X, cursor.Y)) return;
        if (hit.FilePath == null && hit.Url == null && !S.EnlargeScreenPictures) return;

        _hit = hit;
        int generation = ++_generation;
        _loading?.Cancel();
        var cts = _loading = new CancellationTokenSource();
        var image = await ImageLoader.LoadAsync(hit, cts.Token);
        if (generation != _generation || image == null) return;
        Show(hit, image);
    }

    private void Show(ImageHit hit, LoadedImage image)
    {
        var cursor = WindowInfo.CursorPosition();
        double dpi = WindowInfo.ScaleAt(cursor);
        var work = WindowInfo.WorkAreaAt(cursor);
        var chrome = ImagePreviewWindow.ChromeDip;
        // Real pictures are shown at most at real size; screen captures may be enlarged up to 3x.
        double maxScale = image.IsScreenCapture ? 3 : 1;
        var layout = ImageZoomLogic.Place(image.PixelWidth, image.PixelHeight, maxScale,
            new PixelRect(work.Left, work.Top, work.Width, work.Height), cursor.X, cursor.Y,
            ((int)Math.Round(chrome.Width * dpi), (int)Math.Round(chrome.Height * dpi)), (int)Math.Round(12 * dpi));

        // Nothing to gain if the preview would not be clearly bigger than the picture on screen.
        double shownW = image.PixelWidth * layout.Scale, shownH = image.PixelHeight * layout.Scale;
        if (shownW < hit.Bounds.Width * 1.15 && shownH < hit.Bounds.Height * 1.15) return;

        _image = image;
        _window ??= new ImagePreviewWindow();
        _window.ShowImage(image, layout, dpi, Hint());
        _hotkeys.Set(SaveHotkeyName, S.SaveHotkey, () => _window.Dispatcher.BeginInvoke(Save));
        _hotkeys.Set(CopyHotkeyName, S.CopyHotkey, () => _window.Dispatcher.BeginInvoke(Copy));
    }

    private string Hint()
    {
        var parts = new System.Collections.Generic.List<string>();
        if (Hotkey.TryParse(S.SaveHotkey, out var save)) parts.Add($"{save} save");
        if (Hotkey.TryParse(S.CopyHotkey, out var copy)) parts.Add($"{copy} copy");
        return string.Join("  ·  ", parts);
    }

    public void Hide()
    {
        _generation++;
        _loading?.Cancel();
        _loading = null;
        bool hadShortcuts = _image != null;
        _hit = null;
        _image = null;
        _window?.HidePreview();
        if (hadShortcuts)
        {
            _hotkeys.Remove(SaveHotkeyName);
            _hotkeys.Remove(CopyHotkeyName);
        }
    }

    private void Save()
    {
        if (_image == null || _window == null) return;
        InputSender.MaskMenuKey(); // Alt is still held: don't open the app's menu bar when it is released
        try
        {
            var path = ImageLoader.Save(_image);
            _window.ShowMessage($"Saved to Downloads as {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            Log.Error("Could not save the picture", ex);
            _window.ShowMessage("Could not save the picture", error: true);
        }
    }

    private void Copy()
    {
        if (_image == null || _window == null) return;
        InputSender.MaskMenuKey();
        try
        {
            ImageLoader.Copy(_image);
            _window.ShowMessage("Copied");
        }
        catch (Exception ex)
        {
            Log.Error("Could not copy the picture", ex);
            _window.ShowMessage("Could not copy the picture", error: true);
        }
    }

    /// <summary>From the mouse hook (any thread): scrolling or clicking closes the preview, the picture has moved.</summary>
    public void OnMouse(MouseEventKind kind)
    {
        if (kind is MouseEventKind.Wheel or MouseEventKind.LeftDown or MouseEventKind.OtherButtonDown)
            _poll.Dispatcher.BeginInvoke(() =>
            {
                if (_hit != null) Hide();
            });
    }

    private bool ModifierHeld() => S.Modifier switch
    {
        SelectionModifier.Alt => InputSender.IsDown(InputSender.VK_MENU),
        SelectionModifier.Ctrl => InputSender.IsDown(InputSender.VK_CONTROL),
        SelectionModifier.Shift => InputSender.IsDown(InputSender.VK_SHIFT),
        _ => true,
    };

    private static bool ButtonDown() => InputSender.IsDown(0x01) || InputSender.IsDown(0x02) || InputSender.IsDown(0x04);

    public void Dispose()
    {
        _poll.Stop();
        Hide();
        _finder?.Dispose();
        _window?.Close();
    }
}
