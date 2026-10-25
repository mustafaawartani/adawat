using System;
using System.Windows;
using System.Windows.Input;
using Adawat.Core;
using Wpf.Ui.Controls;

namespace Adawat.UI;

/// <summary>A button that records a new keyboard shortcut when clicked.</summary>
public sealed class HotkeyBox : Button
{
    private readonly Action<string> _changed;
    private readonly HotkeyManager _hotkeys;
    private string _value;
    private bool _recording;

    public HotkeyBox(string value, HotkeyManager hotkeys, Action<string> changed)
    {
        _value = value;
        _hotkeys = hotkeys;
        _changed = changed;
        MinWidth = 150;
        Appearance = ControlAppearance.Secondary;
        Icon = Ui.Icon(SymbolRegular.Keyboard24);
        Focusable = true;
        ToolTip = "Click, then press the new shortcut. Esc cancels, Backspace turns it off.";
        UpdateText();

        Click += (_, _) => StartRecording();
        LostKeyboardFocus += (_, _) => StopRecording();
        PreviewKeyDown += OnKeyDown;
    }

    private void UpdateText() => Content = _recording ? "Press keys…" : (string.IsNullOrEmpty(_value) ? "Off" : _value.Replace("+", " + "));

    private void StartRecording()
    {
        if (_recording) return;
        _recording = true;
        _hotkeys.Suspend(); // otherwise our own shortcuts would swallow the key press
        Keyboard.Focus(this);
        UpdateText();
    }

    private void StopRecording()
    {
        if (!_recording) return;
        _recording = false;
        _hotkeys.Resume();
        UpdateText();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            StopRecording();
            return;
        }
        if (key == Key.Back || key == Key.Delete)
        {
            _value = "";
            StopRecording();
            _changed(_value);
            return;
        }

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (Hotkey.IsModifierKey(vk) || !Hotkey.IsKnownKey(vk)) return; // wait for the main key

        var mods = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= HotkeyModifiers.Win;
        bool isFunctionKey = vk >= 0x70 && vk <= 0x87;
        if (mods == HotkeyModifiers.None && !isFunctionKey) return; // a plain letter would break typing

        _value = new Hotkey(mods, vk).ToString();
        StopRecording();
        _changed(_value);
    }
}
