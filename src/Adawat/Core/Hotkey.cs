using System;
using System.Collections.Generic;
using System.Linq;

namespace Adawat.Core;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A keyboard shortcut such as "Alt+1" or "Ctrl+Shift+T".</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, int VirtualKey)
{
    private static readonly Dictionary<int, string> Names = BuildNames();
    private static readonly Dictionary<string, int> Keys =
        Names.GroupBy(p => p.Value, StringComparer.OrdinalIgnoreCase)
             .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => VirtualKey == 0;

    private static Dictionary<int, string> BuildNames()
    {
        var names = new Dictionary<int, string>();
        for (int i = 0; i <= 9; i++) names[0x30 + i] = i.ToString();
        for (char c = 'A'; c <= 'Z'; c++) names[c] = c.ToString();
        for (int i = 1; i <= 24; i++) names[0x6F + i] = "F" + i;
        for (int i = 0; i <= 9; i++) names[0x60 + i] = "Num" + i;
        names[0x08] = "Backspace";
        names[0x09] = "Tab";
        names[0x0D] = "Enter";
        names[0x13] = "Pause";
        names[0x1B] = "Esc";
        names[0x20] = "Space";
        names[0x21] = "PageUp";
        names[0x22] = "PageDown";
        names[0x23] = "End";
        names[0x24] = "Home";
        names[0x25] = "Left";
        names[0x26] = "Up";
        names[0x27] = "Right";
        names[0x28] = "Down";
        names[0x2C] = "PrintScreen";
        names[0x2D] = "Insert";
        names[0x2E] = "Delete";
        names[0x6A] = "NumMultiply";
        names[0x6B] = "NumPlus";
        names[0x6D] = "NumMinus";
        names[0x6E] = "NumDecimal";
        names[0x6F] = "NumDivide";
        names[0xBA] = ";";
        names[0xBB] = "=";
        names[0xBC] = ",";
        names[0xBD] = "-";
        names[0xBE] = ".";
        names[0xBF] = "/";
        names[0xC0] = "`";
        names[0xDB] = "[";
        names[0xDC] = "\\";
        names[0xDD] = "]";
        names[0xDE] = "'";
        return names;
    }

    public static bool IsModifierKey(int vk) =>
        vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;

    public static bool IsKnownKey(int vk) => Names.ContainsKey(vk);

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var mods = HotkeyModifiers.None;
        int vk = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "alt": mods |= HotkeyModifiers.Alt; continue;
                case "ctrl":
                case "control": mods |= HotkeyModifiers.Ctrl; continue;
                case "shift": mods |= HotkeyModifiers.Shift; continue;
                case "win":
                case "windows": mods |= HotkeyModifiers.Win; continue;
            }
            if (vk != 0 || !Keys.TryGetValue(part, out vk)) return false;
        }
        if (vk == 0) return false;
        hotkey = new Hotkey(mods, vk);
        return true;
    }

    public override string ToString()
    {
        if (IsEmpty) return "";
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Names.TryGetValue(VirtualKey, out var name) ? name : $"0x{VirtualKey:X2}");
        return string.Join("+", parts);
    }
}
