using System;
using System.Collections.Generic;
using Adawat.Interop;

namespace Adawat.Core;

/// <summary>Registers system-wide shortcuts with RegisterHotKey.</summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly MessageWindow _window;
    private readonly Dictionary<int, (Hotkey Key, Action Callback)> _registered = new();
    private readonly Dictionary<string, (Hotkey Key, Action Callback)> _wanted = new();
    private readonly Dictionary<string, int> _ids = new();
    private int _nextId = 1;
    private bool _suspended;

    /// <summary>Names of shortcuts that another app already owns.</summary>
    public HashSet<string> Conflicts { get; } = new();

    public event Action? ConflictsChanged;

    public HotkeyManager(MessageWindow window)
    {
        _window = window;
        _window.Message += OnMessage;
    }

    /// <summary>Sets (or clears, with an empty/invalid text) the shortcut for <paramref name="name"/>.</summary>
    public void Set(string name, string? hotkeyText, Action callback)
    {
        if (Hotkey.TryParse(hotkeyText, out var key)) _wanted[name] = (key, callback);
        else _wanted.Remove(name);
        Apply();
    }

    public void Remove(string name)
    {
        _wanted.Remove(name);
        Apply();
    }

    /// <summary>Temporarily releases all shortcuts, e.g. while the user records a new one.</summary>
    public void Suspend()
    {
        _suspended = true;
        Apply();
    }

    public void Resume()
    {
        _suspended = false;
        Apply();
    }

    private void Apply()
    {
        foreach (var id in _registered.Keys) Native.UnregisterHotKey(_window.Handle, id);
        _registered.Clear();
        Conflicts.Clear();

        if (!_suspended)
        {
            foreach (var (name, entry) in _wanted)
            {
                if (!_ids.TryGetValue(name, out var id)) _ids[name] = id = _nextId++;
                uint mods = Native.MOD_NOREPEAT;
                if (entry.Key.Modifiers.HasFlag(HotkeyModifiers.Alt)) mods |= Native.MOD_ALT;
                if (entry.Key.Modifiers.HasFlag(HotkeyModifiers.Ctrl)) mods |= Native.MOD_CONTROL;
                if (entry.Key.Modifiers.HasFlag(HotkeyModifiers.Shift)) mods |= Native.MOD_SHIFT;
                if (entry.Key.Modifiers.HasFlag(HotkeyModifiers.Win)) mods |= Native.MOD_WIN;

                if (Native.RegisterHotKey(_window.Handle, id, mods, (uint)entry.Key.VirtualKey))
                {
                    _registered[id] = entry;
                }
                else
                {
                    Conflicts.Add(name);
                    Log.Info($"Shortcut {entry.Key} ({name}) is already used by another app");
                }
            }
        }
        ConflictsChanged?.Invoke();
    }

    private void OnMessage(int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.WM_HOTKEY) return;
        if (_registered.TryGetValue(wParam.ToInt32(), out var entry))
        {
            handled = true;
            entry.Callback();
        }
    }

    public void Dispose()
    {
        _wanted.Clear();
        _suspended = true;
        Apply();
        _window.Message -= OnMessage;
    }
}
