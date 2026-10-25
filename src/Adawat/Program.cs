using System;
using System.Linq;
using System.Runtime.InteropServices;
using Adawat.Core;

namespace Adawat;

public static class Program
{
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    [STAThread]
    public static int Main(string[] args)
    {
        // Elevated helper launched by the "Run as administrator" setting.
        if (args.Length > 0 && (args[0] == StartupManager.SetupTaskArgument || args[0] == StartupManager.RemoveTaskArgument))
        {
            return StartupManager.HandleHelperArguments(args);
        }

        var instance = SingleInstance.Acquire();
        if (!instance.IsFirst)
        {
            instance.Dispose();
            if (args.Contains("--startup")) return 0; // already running
            // Let the running copy bring its window to the front.
            AllowSetForegroundWindow(-1);
            SingleInstance.Forward(args.Length == 0 ? new[] { "--show-settings" } : args);
            return 0;
        }

        try
        {
            var app = new App(args, instance);
            app.Run();
        }
        catch (Exception ex)
        {
            Log.Error("Adawat crashed", ex);
            return 1;
        }
        finally
        {
            instance.Dispose();
        }
        return 0;
    }
}
