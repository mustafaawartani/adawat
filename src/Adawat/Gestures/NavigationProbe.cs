using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using Adawat.Core;

namespace Adawat.Gestures;

/// <summary>
/// Looks at the window under the pointer (through Windows accessibility) for its back and forward
/// buttons, to tell whether a swipe would do anything there. Runs on its own thread, gives up after a
/// moment, and answers "unknown" when in doubt so a swipe is never lost because an app is slow.
/// </summary>
public static class NavigationProbe
{
    public readonly record struct Result(NavigationState Back, NavigationState Forward)
    {
        public NavigationState For(bool forward) => forward ? Forward : Back;
    }

    private static readonly Result Unknown = new(NavigationState.Unknown, NavigationState.Unknown);
    private static int _running;

    public static Task<Result> CheckAsync(IntPtr window)
    {
        if (window == IntPtr.Zero || Interlocked.Exchange(ref _running, 1) == 1) return Task.FromResult(Unknown);
        var done = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.TrySetResult(Probe(window));
            }
            catch (Exception ex)
            {
                Log.Info($"Could not look for back/forward buttons: {ex.Message}");
                done.TrySetResult(Unknown);
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        })
        {
            IsBackground = true,
            Name = "Adawat back button probe",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return done.Task;
    }

    private static Result Probe(IntPtr window)
    {
        var root = AutomationElement.FromHandle(window);
        var cache = new CacheRequest { AutomationElementMode = AutomationElementMode.None, TreeScope = TreeScope.Element };
        cache.Add(AutomationElement.NameProperty);
        cache.Add(AutomationElement.AutomationIdProperty);
        cache.Add(AutomationElement.IsEnabledProperty);
        cache.Add(AutomationElement.IsOffscreenProperty);
        AutomationElementCollection found;
        using (cache.Activate())
        {
            found = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        }
        var buttons = new List<SeenButton>();
        foreach (AutomationElement element in found)
        {
            try
            {
                var info = element.Cached;
                if (info.IsOffscreen) continue;
                buttons.Add(new SeenButton(info.Name ?? "", info.AutomationId ?? "", info.IsEnabled));
            }
            catch
            {
                // The button went away meanwhile.
            }
        }
        return new Result(NavigationButtons.Classify(buttons, forward: false), NavigationButtons.Classify(buttons, forward: true));
    }
}
