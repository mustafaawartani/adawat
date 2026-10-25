using System;
using System.Diagnostics;
using System.IO;

namespace Adawat.Core;

public static class AppPaths
{
    public const string AppName = "Adawat";
    public const string ReleasesUrl = "https://github.com/mustafaawartani/adawat/releases/latest";

    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string ExecutablePath { get; } =
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "Adawat.exe";

    public static string Version
    {
        get
        {
            var v = typeof(AppPaths).Assembly.GetName().Version;
            return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }
}

/// <summary>Tiny file logger (%AppData%\Adawat\adawat.log), rolled at 1 MB.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string FilePath => Path.Combine(AppPaths.DataFolder, "adawat.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.DataFolder);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1_000_000)
                {
                    File.Move(FilePath, FilePath + ".old", true);
                }
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break the app.
        }
    }
}
