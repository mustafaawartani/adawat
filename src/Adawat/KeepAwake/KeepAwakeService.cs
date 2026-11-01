using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Adawat.Core;

namespace Adawat.KeepAwake;

/// <summary>
/// Stay awake: keeps the PC working for a while (30 minutes, an hour, until a time or until turned
/// off), also with the lid closed, so downloads, uploads and programs go on and Wi-Fi stays connected.
/// Afterwards the PC may sleep again; if the lid is closed by then, it goes to sleep right away.
///
/// The idle sleep is held off with a power request (like PowerToys Awake). Closing the lid is a
/// Windows setting ("When I close the lid"), so it is set to "Do nothing" while staying awake and
/// put back exactly as it was afterwards, also after a crash (the old values are kept in the settings).
/// </summary>
public sealed class KeepAwakeService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly MessageWindow _window;
    private readonly Action _save;
    private readonly DispatcherTimer _timer;
    private IntPtr _lidNotification;
    private bool _lidClosed;
    private bool _requested;

    /// <summary>Raised on the UI thread when staying awake starts, ends or counts down.</summary>
    public event Action? Changed;

    /// <summary>Raised when it could not do something the user asked for (message for the user).</summary>
    public event Action<string, string>? Problem;

    private KeepAwakeSettings S => _settings.KeepAwake;

    public KeepAwakeService(AppSettings settings, MessageWindow window, Action save)
    {
        _settings = settings;
        _window = window;
        _save = save;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += (_, _) => Tick();
        _window.Message += OnMessage;
        _lidNotification = PowerApi.RegisterPowerSettingNotification(_window.Handle, ref PowerApi.LidSwitchStateChange, 0);

        if (S.Active && (S.Until == null || S.Until > DateTime.UtcNow))
        {
            Log.Info("Stay awake: going on after a restart");
            Engage();
        }
        else
        {
            S.Active = false;
            S.Until = null;
            RestoreLid(); // Adawat may have closed while staying awake
        }
    }

    public bool IsOn => S.Active;

    /// <summary>When it ends (local time), or null when it lasts until turned off (or is off).</summary>
    public DateTime? EndsAt => S.Active && S.Until is { } until ? until.ToLocalTime() : null;

    /// <summary>"Off", "On until you turn it off", "On · 42 min left (until 15:40)".</summary>
    public string Status
    {
        get
        {
            if (!S.Active) return "Off";
            if (EndsAt is not { } end) return "On until you turn it off";
            return $"On · {Remaining(end - DateTime.Now)} left (until {end.ToString("t", CultureInfo.CurrentCulture)})";
        }
    }

    public static string Remaining(TimeSpan left)
    {
        if (left < TimeSpan.FromMinutes(1)) return "less than a minute";
        int total = (int)Math.Ceiling(left.TotalMinutes);
        int hours = total / 60, minutes = total % 60;
        if (hours == 0) return $"{minutes} min";
        return minutes == 0 ? $"{hours} h" : $"{hours} h {minutes} min";
    }

    public static string LengthName(int minutes) => minutes switch
    {
        0 => "Until I turn it off",
        < 60 => $"{minutes} minutes",
        60 => "1 hour",
        _ when minutes % 60 == 0 => $"{minutes / 60} hours",
        _ => Remaining(TimeSpan.FromMinutes(minutes)),
    };

    /// <summary>Starts (or restarts) staying awake with the length chosen in the settings.</summary>
    public void StartWithSettings()
    {
        switch (S.Minutes)
        {
            case KeepAwakeSettings.CustomLength:
                Start(Math.Clamp(S.CustomMinutes, 1, 24 * 60));
                break;
            case KeepAwakeSettings.UntilClockTime:
                if (TryParseClock(S.UntilClock, DateTime.Now, out var end)) StartUntil(end);
                else Problem?.Invoke("Stay awake: that time is not clear", "Write the time like 18:30 or 6:30 PM.");
                break;
            default:
                Start(Math.Max(0, S.Minutes));
                break;
        }
    }

    /// <summary>Stays awake for <paramref name="minutes"/> (0 = until turned off).</summary>
    public void Start(int minutes) => Begin(minutes > 0 ? DateTime.UtcNow.AddMinutes(minutes) : null);

    public void StartUntil(DateTime localEnd) => Begin(localEnd.ToUniversalTime());

    /// <summary>The next time the clock shows <paramref name="text"/> ("18:30", "6:30 PM"), today or tomorrow.</summary>
    public static bool TryParseClock(string text, DateTime now, out DateTime end)
    {
        end = default;
        if (!DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.NoCurrentDateDefault, out var parsed)
            && !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out parsed))
            return false;
        end = now.Date + parsed.TimeOfDay;
        if (end <= now) end = end.AddDays(1);
        return true;
    }

    private void Begin(DateTime? untilUtc)
    {
        S.Active = true;
        S.Until = untilUtc;
        Engage();
        _save();
        Log.Info($"Stay awake: on, {(untilUtc is { } u ? "until " + u.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "until turned off")}");
        Changed?.Invoke();
    }

    /// <summary>Ends staying awake: the PC may sleep again, and the lid setting is put back.</summary>
    public void Stop() => End(timeUp: false);

    /// <summary>The settings changed (lid, screen): apply them to the running stay-awake.</summary>
    public void Apply()
    {
        if (!S.Active) return;
        Engage();
        Changed?.Invoke();
    }

    private void Engage()
    {
        uint flags = PowerApi.ES_CONTINUOUS | PowerApi.ES_SYSTEM_REQUIRED | (S.KeepScreenOn ? PowerApi.ES_DISPLAY_REQUIRED : 0);
        _requested = PowerApi.SetThreadExecutionState(flags) != 0;
        if (!_requested) Log.Error("Stay awake: Windows did not take the request to stay awake");

        if (S.WhileLidClosed) KeepWorkingWithLidClosed();
        else RestoreLid();
        _timer.Start();
    }

    private void Tick()
    {
        if (!S.Active)
        {
            _timer.Stop();
            return;
        }
        if (S.Until is { } until && DateTime.UtcNow >= until) End(timeUp: true);
        else Changed?.Invoke();
    }

    private void End(bool timeUp)
    {
        if (!S.Active && !_requested) return;
        S.Active = false;
        S.Until = null;
        _timer.Stop();
        PowerApi.SetThreadExecutionState(PowerApi.ES_CONTINUOUS);
        _requested = false;
        RestoreLid();
        _save();
        Log.Info(timeUp ? "Stay awake: time is up" : "Stay awake: turned off");
        Changed?.Invoke();

        if (timeUp)
        {
            if (_lidClosed && S.SleepWhenDoneIfLidClosed)
            {
                Log.Info("Stay awake: the lid is closed, going to sleep");
                PowerApi.Sleep();
            }
            else
            {
                UI.Toast.Show("Stay awake has ended", "Your PC can sleep again as usual.", seconds: 5);
            }
        }
    }

    // ---- The lid ---------------------------------------------------------------------------

    private void KeepWorkingWithLidClosed()
    {
        if (!PowerApi.TryGetActiveScheme(out var scheme)) return;
        if (S.LidBackup is { } backup && backup.Scheme == scheme) return; // already done for this plan
        if (S.LidBackup != null) RestoreLid(); // the power plan was switched: put the old one back first

        if (!PowerApi.TryReadLid(scheme, out uint ac, out uint dc))
        {
            Log.Error("Stay awake: could not read the lid setting");
            return;
        }
        S.LidBackup = new LidBackup { Scheme = scheme, PluggedIn = ac, OnBattery = dc };
        _save();
        if (ac == PowerApi.LidDoNothing && dc == PowerApi.LidDoNothing) return;
        if (!PowerApi.TryWriteLid(scheme, PowerApi.LidDoNothing, PowerApi.LidDoNothing, out uint error))
        {
            S.LidBackup = null;
            _save();
            Log.Error($"Stay awake: Windows did not let Adawat change the lid setting (error {error})");
            Problem?.Invoke("Closing the lid will still put the PC to sleep",
                "Windows did not let Adawat change what closing the lid does. Turn on \"Run as administrator\" in Adawat's settings, " +
                "or set \"When I close the lid\" to \"Do nothing\" in Control Panel → Power Options.");
            return;
        }
        Log.Info($"Stay awake: closing the lid now does nothing (was {ac} plugged in, {dc} on battery)");
    }

    private void RestoreLid()
    {
        if (S.LidBackup is not { } backup) return;
        if (PowerApi.TryWriteLid(backup.Scheme, backup.PluggedIn, backup.OnBattery, out uint error))
        {
            Log.Info("Stay awake: the lid setting is back as it was");
            S.LidBackup = null;
            _save();
        }
        else
        {
            // Keep the backup: it is tried again next time.
            Log.Error($"Stay awake: could not put the lid setting back (error {error})");
        }
    }

    private void OnMessage(int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != PowerApi.WM_POWERBROADCAST || wParam.ToInt64() != PowerApi.PBT_POWERSETTINGCHANGE || lParam == IntPtr.Zero) return;
        var setting = Marshal.PtrToStructure<PowerApi.POWERBROADCAST_SETTING>(lParam);
        if (setting.PowerSetting != PowerApi.LidSwitchStateChange || setting.DataLength < 1) return;
        _lidClosed = setting.Data == 0;
    }

    public void Dispose()
    {
        _timer.Stop();
        _window.Message -= OnMessage;
        if (_lidNotification != IntPtr.Zero) PowerApi.UnregisterPowerSettingNotification(_lidNotification);
        // Adawat is closing: the PC may sleep and the lid works as usual again. The "on" state stays
        // saved, so it goes on when Adawat starts again (unless its time is over).
        PowerApi.SetThreadExecutionState(PowerApi.ES_CONTINUOUS);
        bool active = S.Active;
        RestoreLid();
        S.Active = active;
    }
}

/// <summary>Windows power functions used by <see cref="KeepAwakeService"/>.</summary>
internal static class PowerApi
{
    public const uint ES_CONTINUOUS = 0x80000000;
    public const uint ES_SYSTEM_REQUIRED = 0x00000001;
    public const uint ES_DISPLAY_REQUIRED = 0x00000002;

    public const int WM_POWERBROADCAST = 0x0218;
    public const int PBT_POWERSETTINGCHANGE = 0x8013;

    public const uint LidDoNothing = 0;

    public static Guid LidSwitchStateChange = new("BA3E0F4D-B817-4094-A2D1-D56379E6A0F3");
    private static Guid ButtonsSubgroup = new("4F971E89-EEBD-4455-A8DE-9E59040E7347");
    private static Guid LidCloseAction = new("5CA83367-6E45-459F-A27B-476B1D01C936");

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    [DllImport("kernel32.dll")]
    public static extern uint SetThreadExecutionState(uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid powerSetting, int flags);

    [DllImport("user32.dll")]
    public static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool force, bool disableWakeEvents);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, int msg, IntPtr wParam, IntPtr lParam);

    public static bool TryGetActiveScheme(out Guid scheme)
    {
        scheme = Guid.Empty;
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out var pointer) != 0 || pointer == IntPtr.Zero) return false;
            scheme = Marshal.PtrToStructure<Guid>(pointer);
            LocalFree(pointer);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not read the power plan", ex);
            return false;
        }
    }

    public static bool TryReadLid(Guid scheme, out uint pluggedIn, out uint onBattery)
    {
        pluggedIn = onBattery = 0;
        try
        {
            return PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref ButtonsSubgroup, ref LidCloseAction, out pluggedIn) == 0
                   & PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref ButtonsSubgroup, ref LidCloseAction, out onBattery) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Writes the lid action of <paramref name="scheme"/> and makes Windows use it now.</summary>
    public static bool TryWriteLid(Guid scheme, uint pluggedIn, uint onBattery, out uint error)
    {
        try
        {
            error = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref ButtonsSubgroup, ref LidCloseAction, pluggedIn);
            if (error == 0) error = PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref ButtonsSubgroup, ref LidCloseAction, onBattery);
            if (error != 0) return false;
            // Changes to the plan in use only count once the plan is set again.
            if (TryGetActiveScheme(out var active) && active == scheme) error = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
            return error == 0;
        }
        catch (Exception ex)
        {
            Log.Error("Could not change the lid setting", ex);
            error = 1;
            return false;
        }
    }

    /// <summary>Puts the PC to sleep; where Windows has no classic sleep (modern standby), turns the screen off, which sleeps.</summary>
    public static void Sleep()
    {
        try
        {
            if (SetSuspendState(false, false, false)) return;
        }
        catch
        {
        }
        const int WM_SYSCOMMAND = 0x0112, SC_MONITORPOWER = 0xF170;
        PostMessage(new IntPtr(0xFFFF), WM_SYSCOMMAND, new IntPtr(SC_MONITORPOWER), new IntPtr(2));
    }
}
