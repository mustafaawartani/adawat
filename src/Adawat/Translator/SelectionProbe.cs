using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace Adawat.Translator;

/// <summary>
/// Asks the focused app, through Windows accessibility, whether some text is selected. Used when a
/// mouse drag never passed over text (so the pointer never became a text cursor), to still show the
/// translate button for a real selection without popping it up after dragging files or windows.
/// </summary>
public static class SelectionProbe
{
    private static int _running;

    public static async Task<bool> HasSelectedTextAsync(TimeSpan timeout)
    {
        // One question at a time: a slow app must not pile up waiting threads.
        if (Interlocked.Exchange(ref _running, 1) == 1) return false;
        var probe = new Task<bool>(Probe);
        var thread = new Thread(() =>
        {
            try { probe.RunSynchronously(); }
            finally { Interlocked.Exchange(ref _running, 0); }
        })
        {
            IsBackground = true,
            Name = "Adawat selection probe",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        var finished = await Task.WhenAny(probe, Task.Delay(timeout));
        return finished == probe && probe.Status == TaskStatus.RanToCompletion && probe.Result;
    }

    private static bool Probe()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            for (int depth = 0; element != null && depth < 4; depth++)
            {
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
                {
                    foreach (var range in ((TextPattern)pattern).GetSelection())
                    {
                        if (!string.IsNullOrWhiteSpace(range.GetText(64))) return true;
                    }
                    return false;
                }
                element = TreeWalker.ControlViewWalker.GetParent(element);
            }
        }
        catch
        {
            // The app does not answer accessibility questions: treat as "no selection".
        }
        return false;
    }
}
