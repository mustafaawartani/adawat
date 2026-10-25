using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Adawat.Interop;

namespace Adawat.Core;

/// <summary>Helpers to find out which app a window belongs to and where things are on screen.</summary>
public static class WindowInfo
{
    private static readonly int OwnProcessId = Environment.ProcessId;
    private static readonly Dictionary<uint, string> NameCache = new();

    public static Native.POINT CursorPosition()
    {
        Native.GetCursorPos(out var p);
        return p;
    }

    public static IntPtr RootWindowAt(Native.POINT p)
    {
        var hwnd = Native.WindowFromPoint(p);
        return hwnd == IntPtr.Zero ? IntPtr.Zero : Native.GetAncestor(hwnd, Native.GA_ROOT);
    }

    public static bool IsOwnWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == OwnProcessId;
    }

    /// <summary>Executable name such as "explorer.exe", or "" when unknown.</summary>
    public static string ProcessName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "";
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return "";
        lock (NameCache)
        {
            if (NameCache.TryGetValue(pid, out var cached)) return cached;
            string name;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                name = process.ProcessName + ".exe";
            }
            catch
            {
                name = "";
            }
            if (NameCache.Count > 500) NameCache.Clear();
            NameCache[pid] = name;
            return name;
        }
    }

    public static bool ListContains(IEnumerable<string> apps, string processName)
    {
        if (string.IsNullOrEmpty(processName)) return false;
        var bare = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName[..^4] : processName;
        return apps.Any(a =>
        {
            var entry = a.Trim();
            if (entry.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) entry = entry[..^4];
            return entry.Length > 0 && string.Equals(entry, bare, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>True while a game, video or presentation runs full screen.</summary>
    public static bool IsFullscreenAppRunning()
    {
        try
        {
            return Native.SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Work area (screen minus taskbar) of the monitor containing <paramref name="p"/>, in physical pixels.</summary>
    public static Native.RECT WorkAreaAt(Native.POINT p)
    {
        var monitor = Native.MonitorFromPoint(p, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(monitor, ref info);
        return info.rcWork;
    }

    public static double ScaleAt(Native.POINT p)
    {
        try
        {
            var monitor = Native.MonitorFromPoint(p, Native.MONITOR_DEFAULTTONEAREST);
            if (Native.GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0) return dpi / 96.0;
        }
        catch
        {
        }
        return 1.0;
    }

    /// <summary>
    /// True when the mouse cursor looks like a text cursor. Apps with their own I-beam
    /// (Acrobat, some editors) use custom cursors, so anything that is not a standard
    /// arrow/hand/resize/busy cursor also counts.
    /// </summary>
    public static bool IsLikelyTextCursor()
    {
        var info = new Native.CURSORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.CURSORINFO>() };
        if (!Native.GetCursorInfo(ref info) || info.hCursor == IntPtr.Zero) return false;
        if (info.hCursor == IBeam) return true;
        return !NonTextCursors.Contains(info.hCursor);
    }

    private static readonly IntPtr IBeam = Native.LoadCursor(IntPtr.Zero, new IntPtr(Native.IDC_IBEAM));

    // IDC_ARROW, WAIT, CROSS, SIZENWSE, SIZENESW, SIZEWE, SIZENS, SIZEALL, NO, HAND, APPSTARTING, HELP
    private static readonly HashSet<IntPtr> NonTextCursors = new[] { 32512, 32514, 32515, 32642, 32643, 32644, 32645, 32646, 32648, 32649, 32650, 32651 }
        .Select(id => Native.LoadCursor(IntPtr.Zero, new IntPtr(id)))
        .Where(h => h != IntPtr.Zero)
        .ToHashSet();
}
