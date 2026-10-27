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

    /// <summary>The picture at the physical screen point, or null.</summary>
    public Task<ImageHit?> FindAsync(Native.POINT point, int minSize)
    {
        var result = new TaskCompletionSource<ImageHit?>();
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
        for (int depth = 0; start != null && depth < 5; depth++, start = SafeParent(walker, start))
        {
            var info = start.Current;
            var type = info.ControlType;
            chain.Add(type.ProgrammaticName.Replace("ControlType.", ""));
            var bounds = ToPixels(info.BoundingRectangle);
            if (bounds == null) continue;

            if (type == ControlType.Image)
            {
                if (bounds.Value.Width < minSize || bounds.Value.Height < minSize)
                {
                    Trace($"Picture in {process} is smaller than {minSize} px ({bounds.Value.Width}×{bounds.Value.Height})");
                    return null;
                }
                var url = ImageUrl(start, walker);
                return new ImageHit(bounds.Value, null, url, info.Name);
            }

            // Stop at things that are clearly not part of a picture.
            if (type == ControlType.Window || type == ControlType.Document || type == ControlType.Pane && depth > 0) break;
        }
        Trace($"No picture in {process} under the pointer ({string.Join(" < ", chain)})");
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
        var own = ValueOf(image);
        if (ImageZoomLogic.LooksLikeImageUrl(own)) return own;
        var parent = SafeParent(walker, image);
        for (int i = 0; parent != null && i < 3; i++, parent = SafeParent(walker, parent))
        {
            if (parent.Current.ControlType != ControlType.Hyperlink) continue;
            var href = ValueOf(parent);
            return ImageZoomLogic.LooksLikeImageUrl(href) ? href : null;
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
