using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Adawat.Core;
using Adawat.Interop;
using Adawat.UI;

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
    private long _lookupMs;
    private int _retries;
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
        // A pointer that shakes by a pixel or two (touchpads do) still counts as resting.
        if (Math.Abs(p.X - _lastPos.X) > RestSlack || Math.Abs(p.Y - _lastPos.Y) > RestSlack)
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
        if (_lastLookup is { } last && Math.Abs(last.X - p.X) <= RestSlack && Math.Abs(last.Y - p.Y) <= RestSlack)
        {
            // Browsers build their accessibility tree only when first asked, and pages still loading
            // add pictures late, so the first look often finds nothing: look a few more times, a
            // little later each time, instead of waiting for the pointer to move.
            if (_retries >= RetryDelaysMs.Length || now - _lookupMs < RetryDelaysMs[_retries]) return;
            _retries++;
        }
        else
        {
            _retries = 0;
        }
        if (!ModifierHeld() || ButtonDown()) return;

        var root = WindowInfo.RootWindowAt(p);
        if (root == IntPtr.Zero || WindowInfo.IsOwnWindow(root)) return;
        if (WindowInfo.ListContains(S.DisabledApps, WindowInfo.ProcessName(root))) return;
        if (S.DisableInFullscreen && !ImageFinder.IsDesktop(root) && WindowInfo.IsFullscreenAppRunning()) return;

        _finder ??= new ImageFinder();
        if (_finder.IsBusy) return; // still waiting for a slow app: ask again once it has answered
        _lastLookup = p;
        _lookupMs = now;
        _ = LookAsync(p);
    }

    private const int RestSlack = 3;
    private static readonly int[] RetryDelaysMs = { 600, 1200, 2500 };

    private async Task LookAsync(Native.POINT p)
    {
        _looking = true;
        ImageHit? hit;
        try
        {
            var find = _finder!.FindAsync(p, Math.Max(16, S.MinSize));
            hit = await Task.WhenAny(find, Task.Delay(3000)) == find ? find.Result : null;
        }
        finally
        {
            _looking = false;
        }

        var cursor = WindowInfo.CursorPosition();
        if (hit == null || _hit != null || !hit.Bounds.Contains(cursor.X, cursor.Y, slack: RestSlack)) return;
        if (hit.FilePath == null && hit.Url == null && !S.EnlargeScreenPictures) return;
        Log.Info($"Picture zoom: opening {hit.FilePath ?? hit.Url ?? "a picture from the screen"} ({hit.Bounds.Width}×{hit.Bounds.Height} on screen)");

        _hit = hit;
        int generation = ++_generation;
        _loading?.Cancel();
        var cts = _loading = new CancellationTokenSource();
        var image = await ImageLoader.LoadAsync(hit, cts.Token);
        if (generation != _generation) return;
        if (image == null)
        {
            Log.Info("Picture zoom: the picture could not be loaded");
            return;
        }
        Show(hit, image);
    }

    private void Show(ImageHit hit, LoadedImage image)
    {
        var cursor = WindowInfo.CursorPosition();
        double dpi = WindowInfo.ScaleAt(cursor);
        var work = WindowInfo.WorkAreaAt(cursor);
        var chrome = ImagePreviewWindow.ChromeDip;
        // Big pictures are shown at real size (or as big as the screen allows). Small ones, and copies
        // of the screen, are enlarged so the preview is clearly bigger than the picture on the page.
        double maxScale = ImageZoomLogic.MaxPreviewScale(image.PixelWidth, image.PixelHeight, hit.Bounds.Width, hit.Bounds.Height,
            (int)Math.Round(ImageZoomLogic.MinPreviewSideDip * dpi));
        var layout = ImageZoomLogic.Place(image.PixelWidth, image.PixelHeight, maxScale,
            new PixelRect(work.Left, work.Top, work.Width, work.Height), cursor.X, cursor.Y,
            ((int)Math.Round(chrome.Width * dpi), (int)Math.Round(chrome.Height * dpi)), (int)Math.Round(12 * dpi));

        // Nothing to gain if the preview would not be clearly bigger than the picture on screen.
        double shownW = image.PixelWidth * layout.Scale, shownH = image.PixelHeight * layout.Scale;
        if (shownW < hit.Bounds.Width * 1.15 && shownH < hit.Bounds.Height * 1.15)
        {
            Log.Info($"Picture zoom: the picture already fills the screen ({hit.Bounds.Width}×{hit.Bounds.Height}); nothing to enlarge");
            return;
        }

        _image = image;
        _window ??= new ImagePreviewWindow();
        _window.ShowImage(image, layout, dpi, Hint());
        _hotkeys.Set(SaveHotkeyName, S.SaveHotkey, () => _window.Dispatcher.BeginInvoke(Save));
        _hotkeys.Set(CopyHotkeyName, S.CopyHotkey, () => _window.Dispatcher.BeginInvoke(Copy));
        var taken = new System.Collections.Generic.List<string>();
        if (_hotkeys.Conflicts.Contains(SaveHotkeyName)) taken.Add(S.SaveHotkey);
        if (_hotkeys.Conflicts.Contains(CopyHotkeyName)) taken.Add(S.CopyHotkey);
        if (taken.Count > 0 && !_warnedConflict)
        {
            _warnedConflict = true;
            Log.Info($"Picture zoom: shortcut {string.Join(", ", taken)} is used by another app");
            Toast.Show($"{string.Join(" and ", taken)} is used by another app", "Pick another shortcut in Adawat Settings → Picture zoom.", success: false, seconds: 5);
        }
    }

    private bool _warnedConflict;

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
            Log.Info($"Picture zoom: saved {path}");
            _window.ShowMessage("Saved to Downloads");
            Toast.Show("Saved to Downloads", Path.GetFileName(path) + "  ·  click to show it",
                onClick: () => OpenFolderAndSelect(path));
        }
        catch (Exception ex)
        {
            Log.Error("Could not save the picture", ex);
            _window.ShowMessage("Could not save the picture", error: true);
            Toast.Show("Could not save the picture", ex.Message, success: false);
        }
    }

    private void Copy()
    {
        if (_image == null || _window == null) return;
        InputSender.MaskMenuKey();
        try
        {
            ImageLoader.Copy(_image);
            Log.Info("Picture zoom: copied the picture");
            _window.ShowMessage("Copied");
            Toast.Show("Picture copied", $"{_image.PixelWidth} × {_image.PixelHeight}  ·  paste it anywhere with Ctrl+V");
        }
        catch (Exception ex)
        {
            Log.Error("Could not copy the picture", ex);
            _window.ShowMessage("Could not copy the picture", error: true);
            Toast.Show("Could not copy the picture", ex.Message, success: false);
        }
    }

    private static void OpenFolderAndSelect(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Could not open the Downloads folder", ex);
        }
    }

    /// <summary>From the mouse hook (any thread): scrolling or clicking closes the preview, the picture has moved.</summary>
    public void OnMouse(MouseEventKind kind)
    {
        if (kind is MouseEventKind.Wheel or MouseEventKind.LeftDown or MouseEventKind.OtherButtonDown)
            _poll.Dispatcher.BeginInvoke(() =>
            {
                if (_hit == null) return;
                Hide();
                // Closed on purpose: open again only once the pointer moves, not a moment later on the same spot.
                _lastLookup = WindowInfo.CursorPosition();
                _retries = RetryDelaysMs.Length;
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
