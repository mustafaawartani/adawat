using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using Adawat.Core;
using Adawat.Interop;

namespace Adawat.ImageZoom;

/// <summary>What is under the pointer: a picture's area on screen and the best source for it.</summary>
public sealed record ImageHit(PixelRect Bounds, string? FilePath, string? Url, string Name);

/// <summary>
/// Finds the picture under the pointer in any app, through Windows accessibility (UI Automation):
/// picture files in File Explorer and on the desktop (their real file), pictures on web pages
/// (the full-size picture when they link to one), and any other picture element.
/// Runs on its own thread, because asking another app can take a moment.
/// </summary>
public sealed class ImageFinder : IDisposable
{
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _thread;
    private int _busy;

    public ImageFinder()
    {
        // The Shell (File Explorer windows) needs a single-threaded apartment.
        _thread = new Thread(() =>
        {
            foreach (var action in _work.GetConsumingEnumerable()) action();
        })
        {
            IsBackground = true,
            Name = "Adawat picture finder",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>True while an app is still being asked about the last point.</summary>
    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    /// <summary>The picture at the physical screen point, or null.</summary>
    public Task<ImageHit?> FindAsync(Native.POINT point, int minSize)
    {
        // An app that answers slowly must not pile up questions behind it: one at a time.
        if (Interlocked.Exchange(ref _busy, 1) == 1) return Task.FromResult<ImageHit?>(null);
        var result = new TaskCompletionSource<ImageHit?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Add(() =>
        {
            try
            {
                result.TrySetResult(Find(point, minSize));
            }
            catch (Exception ex)
            {
                Log.Info($"Picture zoom could not look under the pointer: {ex.Message}");
                result.TrySetResult(null);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        });
        return result.Task;
    }

    private static ImageHit? Find(Native.POINT point, int minSize)
    {
        var root = WindowInfo.RootWindowAt(point);
        if (root == IntPtr.Zero) return null;
        var process = WindowInfo.ProcessName(root);
        bool explorer = string.Equals(process, "explorer.exe", StringComparison.OrdinalIgnoreCase);

        var start = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
        var walker = TreeWalker.ControlViewWalker;

        // File Explorer and the desktop: the file under the pointer (its thumbnail is often a
        // separate picture element on top of the item, so look for the item first).
        if (explorer)
        {
            var element = start;
            for (int depth = 0; element != null && depth < 6; depth++, element = SafeParent(walker, element))
            {
                var info = element.Current;
                if (info.ControlType != ControlType.ListItem && info.ControlType != ControlType.DataItem) continue;
                var bounds = ToPixels(info.BoundingRectangle);
                var path = ExplorerFile(root, info.Name);
                Trace($"File Explorer item \"{info.Name}\" -> {path ?? "not a picture file (or folder not found)"}");
                return path == null || bounds == null ? null : new ImageHit(bounds.Value, path, null, Path.GetFileName(path));
            }
        }

        var chain = new List<string>();
        var containers = new List<AutomationElement>();
        for (int depth = 0; start != null && depth < 5; depth++, start = SafeParent(walker, start))
        {
            var info = start.Current;
            var type = info.ControlType;
            chain.Add(type.ProgrammaticName.Replace("ControlType.", ""));
            var bounds = ToPixels(info.BoundingRectangle);
            if (bounds == null) continue;

            if (type == ControlType.Image) return Hit(start, bounds.Value, info.Name, process, minSize, walker, point);

            // Stop at things that are clearly not part of a picture.
            if (type == ControlType.Window || type == ControlType.Document || type == ControlType.Pane && depth > 0) break;
            containers.Add(start);
        }

        // Pictures on web pages are often covered by something else (a link, a play button, a
        // shadow layer, a video length label), so the pointer is "on" that and not on the picture.
        // Look below the nearest small containers for a picture under the pointer.
        var screenArea = (double)System.Windows.Forms.SystemInformation.VirtualScreen.Width * System.Windows.Forms.SystemInformation.VirtualScreen.Height;
        foreach (var container in containers.Take(4))
        {
            var box = ToPixels(SafeBounds(container));
            if (box == null || (double)box.Value.Width * box.Value.Height > screenArea * 0.4) break;
            var covered = PictureBelow(container, point);
            if (covered != null)
            {
                var (element, bounds, name) = covered.Value;
                return Hit(element, bounds, name, process, minSize, walker, point);
            }
        }
        Trace($"No picture in {process} under the pointer ({string.Join(" < ", chain)})");
        return null;
    }

    private static ImageHit? Hit(AutomationElement image, PixelRect bounds, string name, string process, int minSize, TreeWalker walker, Native.POINT point)
    {
        if (bounds.Width < minSize || bounds.Height < minSize)
        {
            Trace($"Picture in {process} is smaller than {minSize} px ({bounds.Width}×{bounds.Height})");
            return null;
        }
        var url = ImageUrl(image, walker) ?? LegacyImageUrl(point);
        if (url == null) Trace($"Picture in {process} gives no address; it will be enlarged from the screen");
        return new ImageHit(bounds, null, url, name);
    }

    /// <summary>The smallest picture element inside <paramref name="container"/> that is under the point.</summary>
    private static (AutomationElement Element, PixelRect Bounds, string Name)? PictureBelow(AutomationElement container, Native.POINT point)
    {
        try
        {
            var cache = new CacheRequest { TreeScope = TreeScope.Element };
            cache.Add(AutomationElement.BoundingRectangleProperty);
            cache.Add(AutomationElement.NameProperty);
            AutomationElementCollection images;
            using (cache.Activate())
            {
                images = container.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Image));
            }
            (AutomationElement, PixelRect, string)? best = null;
            int count = 0;
            foreach (AutomationElement image in images)
            {
                if (++count > 200) break;
                var bounds = ToPixels(image.Cached.BoundingRectangle);
                if (bounds == null || !bounds.Value.Contains(point.X, point.Y)) continue;
                if (best == null || (long)bounds.Value.Width * bounds.Value.Height < (long)best.Value.Item2.Width * best.Value.Item2.Height)
                    best = (image, bounds.Value, image.Cached.Name ?? "");
            }
            return best;
        }
        catch
        {
            return null;
        }
    }

    private static System.Windows.Rect SafeBounds(AutomationElement element)
    {
        try { return element.Current.BoundingRectangle; } catch { return System.Windows.Rect.Empty; }
    }

    // ---- The picture's address through the older accessibility interface (MSAA) ----------

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromPoint(Native.POINT pt, [MarshalAs(UnmanagedType.Interface)] out Accessibility.IAccessible accessible, out object child);

    private const int ROLE_SYSTEM_GRAPHIC = 0x28;

    /// <summary>
    /// Browsers based on Chromium and Firefox give a picture's address as its "value" in the older
    /// accessibility interface, also when the newer one does not.
    /// </summary>
    private static string? LegacyImageUrl(Native.POINT point)
    {
        try
        {
            if (AccessibleObjectFromPoint(point, out var accessible, out var child) != 0 || accessible == null) return null;
            try
            {
                for (int i = 0; i < 3 && accessible != null; i++)
                {
                    if (accessible.get_accRole(child) is int role && role == ROLE_SYSTEM_GRAPHIC)
                    {
                        var value = accessible.get_accValue(child);
                        return ImageZoomLogic.IsPictureSource(value) ? value : null;
                    }
                    // A picture inside a link: the link is what was hit; its first child may be the picture.
                    if (child is int id && id != 0) break;
                    accessible = accessible.accParent as Accessibility.IAccessible;
                    child = 0;
                }
            }
            finally
            {
                if (accessible != null && Marshal.IsComObject(accessible)) Marshal.ReleaseComObject(accessible);
            }
        }
        catch
        {
        }
        return null;
    }

    private static string _lastTrace = "";
    private static long _lastTraceMs;

    /// <summary>Notes in the log why a picture did or did not open (not repeated, at most every few seconds).</summary>
    private static void Trace(string message)
    {
        long now = Environment.TickCount64;
        if (message == _lastTrace && now - _lastTraceMs < 30000) return;
        if (now - _lastTraceMs < 1500) return;
        _lastTrace = message;
        _lastTraceMs = now;
        Log.Info("Picture zoom: " + message);
    }

    private static AutomationElement? SafeParent(TreeWalker walker, AutomationElement element)
    {
        try { return walker.GetParent(element); } catch { return null; }
    }

    private static PixelRect? ToPixels(System.Windows.Rect r)
    {
        if (r.IsEmpty || double.IsInfinity(r.Width) || r.Width < 1 || r.Height < 1) return null;
        return new PixelRect((int)Math.Round(r.X), (int)Math.Round(r.Y), (int)Math.Round(r.Width), (int)Math.Round(r.Height));
    }

    /// <summary>The address of the full picture: the picture's own value, or the link around it when that points to a picture.</summary>
    private static string? ImageUrl(AutomationElement image, TreeWalker walker)
    {
        // The picture's own address (its src) counts even without an extension: YouTube and Google
        // pictures have none, and used to be enlarged from the screen (blurry) because of that.
        var own = ValueOf(image);
        if (ImageZoomLogic.IsPictureSource(own)) return own;
        var parent = SafeParent(walker, image);
        for (int i = 0; parent != null && i < 3; i++, parent = SafeParent(walker, parent))
        {
            if (parent.Current.ControlType != ControlType.Hyperlink) continue;
            var href = ValueOf(parent);
            return ImageZoomLogic.LooksLikeImageUrl(href) || ImageZoomLogic.IsPictureHost(href) ? href : null;
        }
        return null;
    }

    private static string? ValueOf(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) ? ((ValuePattern)pattern).Current.Value : null;
        }
        catch
        {
            return null;
        }
    }

    // ---- File Explorer and the desktop -------------------------------------------------

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    private static string? ExplorerFile(IntPtr root, string displayName)
    {
        var name = ImageZoomLogic.CleanDisplayName(displayName);
        if (name.Length == 0) return null;
        foreach (var folder in ExplorerFolders(root))
        {
            try
            {
                if (!Directory.Exists(folder)) continue;
                var match = ImageZoomLogic.MatchImageFile(name,
                    Directory.EnumerateFiles(folder, name + "*").Select(Path.GetFileName).OfType<string>());
                if (match != null) return Path.Combine(folder, match);
            }
            catch (Exception ex)
            {
                Log.Info($"Picture zoom could not read {folder}: {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>The folders shown in this Explorer window (all of its tabs), or the desktop folders.</summary>
    private static IEnumerable<string> ExplorerFolders(IntPtr root)
    {
        var name = new StringBuilder(64);
        GetClassName(root, name, name.Capacity);
        var cls = name.ToString();
        if (cls is "Progman" or "WorkerW")
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            yield break;
        }
        if (cls != "CabinetWClass")
        {
            Trace($"Explorer window of kind \"{cls}\" is not a folder window");
            yield break;
        }

        var folders = new List<string>();
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) yield break;
            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                foreach (dynamic window in shell.Windows())
                {
                    try
                    {
                        if (new IntPtr((long)window.HWND) != root) continue;
                        string path = window.Document.Folder.Self.Path;
                        if (!string.IsNullOrEmpty(path)) folders.Add(path);
                    }
                    catch
                    {
                        // Not a folder window (or closing); skip it.
                    }
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception ex)
        {
            Log.Info($"Picture zoom could not ask File Explorer for its folder: {ex.Message}");
        }
        if (folders.Count == 0) Trace("File Explorer did not say which folder it shows");
        foreach (var folder in folders) yield return folder;
    }

    /// <summary>The desktop (its icons belong to these windows).</summary>
    public static bool IsDesktop(IntPtr root)
    {
        var name = new StringBuilder(64);
        GetClassName(root, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW";
    }

    public void Dispose() => _work.CompleteAdding();
}
