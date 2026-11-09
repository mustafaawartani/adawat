using System;
using System.Linq;
using System.Windows.Threading;
using Adawat.Core;
using Adawat.Interop;

namespace Adawat.KeyChanges;

/// <summary>
/// The "change keys" tool: makes one key act as another everywhere, for example Left Alt as
/// Left Ctrl, so copy becomes Alt+C. Pauses while an app from the settings list is in front.
/// </summary>
public sealed class KeyRemapService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly KeyboardHook _hook = new();
    private readonly DispatcherTimer _foregroundWatch;
    private readonly DispatcherTimer _renewHook;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private bool _capturing;

    private KeyRemapSettings S => _settings.KeyChanges;

    public KeyRemapService(AppSettings settings)
    {
        _settings = settings;
        _foregroundWatch = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _foregroundWatch.Tick += (_, _) => UpdatePaused();
        _renewHook = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
        _renewHook.Tick += (_, _) => _hook.Renew();
    }

    public void Apply()
    {
        _hook.ReleaseHeldKeys();
        var rules = S.Enabled
            ? S.Remaps.Where(r => r.Enabled).Select(r => new KeyRule(r.From, r.To, r.Keep.ToHashSet()))
            : Enumerable.Empty<KeyRule>();
        _hook.Remapper.SetRules(rules);
        UpdateHook();
        if (_hook.Remapper.Count > 0 && S.DisabledApps.Count > 0) _foregroundWatch.Start();
        else
        {
            _foregroundWatch.Stop();
            _hook.Remapper.Paused = false;
        }
    }

    private void UpdateHook()
    {
        if (_hook.Remapper.Count > 0 || _capturing) _hook.Start();
        else _hook.Stop();
        if (_hook.Remapper.Count > 0) _renewHook.Start();
        else _renewHook.Stop();
    }

    private void UpdatePaused()
    {
        var foreground = Native.GetForegroundWindow();
        _hook.Remapper.Paused = foreground != IntPtr.Zero && WindowInfo.ListContains(S.DisabledApps, WindowInfo.ProcessName(foreground));
    }

    /// <summary>
    /// Catches the next key pressed anywhere (even Windows, Alt or Caps Lock, which never reach a
    /// window normally) and passes it to <paramref name="onKey"/> on the UI thread. Key changes are
    /// not applied to that press, so the real key is reported.
    /// </summary>
    public void CaptureNextKey(Action<int> onKey)
    {
        _capturing = true;
        UpdateHook();
        _hook.CaptureNext(vk => _dispatcher.BeginInvoke(() =>
        {
            _capturing = false;
            onKey(vk);
            UpdateHook();
        }));
    }

    public void CancelCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        _hook.CaptureNext(null);
        UpdateHook();
    }

    /// <summary>After the PC wakes up or is unlocked: Windows may have dropped the hook.</summary>
    public void Restart() => _hook.Restart();

    public void Dispose() => _hook.Dispose();
}
