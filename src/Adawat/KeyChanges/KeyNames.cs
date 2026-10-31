using System.Collections.Generic;
using System.Linq;

namespace Adawat.KeyChanges;

/// <summary>Readable names for keys (Windows virtual-key codes, with the side for Ctrl, Alt, Shift and Windows).</summary>
public static class KeyNames
{
    private static readonly Dictionary<int, string> Names = Build();

    /// <summary>All keys offered in the lists, in a sensible order.</summary>
    public static IReadOnlyList<(int Key, string Name)> All { get; } = Names.Select(p => (p.Key, p.Value)).ToList();

    public static string Name(int vk) => vk == 0 ? "Nothing (key off)" : Names.TryGetValue(vk, out var name) ? name : $"Key {vk:X2}";

    private static Dictionary<int, string> Build()
    {
        var n = new Dictionary<int, string>
        {
            [0xA2] = "Left Ctrl", [0xA3] = "Right Ctrl",
            [0xA4] = "Left Alt", [0xA5] = "Right Alt",
            [0xA0] = "Left Shift", [0xA1] = "Right Shift",
            [0x5B] = "Left Windows", [0x5C] = "Right Windows", [0x5D] = "Menu",
            [0x14] = "Caps Lock", [0x09] = "Tab", [0x0D] = "Enter", [0x1B] = "Esc", [0x20] = "Space", [0x08] = "Backspace",
            [0x2D] = "Insert", [0x2E] = "Delete", [0x24] = "Home", [0x23] = "End", [0x21] = "Page Up", [0x22] = "Page Down",
            [0x25] = "Left arrow", [0x26] = "Up arrow", [0x27] = "Right arrow", [0x28] = "Down arrow",
            [0x2C] = "Print Screen", [0x91] = "Scroll Lock", [0x13] = "Pause", [0x90] = "Num Lock",
        };
        for (char c = 'A'; c <= 'Z'; c++) n[c] = c.ToString();
        for (int i = 0; i <= 9; i++) n[0x30 + i] = i.ToString();
        for (int i = 1; i <= 24; i++) n[0x6F + i] = "F" + i;
        for (int i = 0; i <= 9; i++) n[0x60 + i] = "Num " + i;
        n[0x6A] = "Num *"; n[0x6B] = "Num +"; n[0x6D] = "Num -"; n[0x6E] = "Num ."; n[0x6F] = "Num /";
        n[0xBA] = ";"; n[0xBB] = "="; n[0xBC] = ","; n[0xBD] = "-"; n[0xBE] = "."; n[0xBF] = "/"; n[0xC0] = "`";
        n[0xDB] = "["; n[0xDC] = "\\"; n[0xDD] = "]"; n[0xDE] = "'"; n[0xE2] = "< >";
        n[0xAD] = "Volume mute"; n[0xAE] = "Volume down"; n[0xAF] = "Volume up";
        n[0xB3] = "Play / pause"; n[0xB0] = "Next track"; n[0xB1] = "Previous track"; n[0xB2] = "Stop media";
        n[0xA6] = "Browser back"; n[0xA7] = "Browser forward";
        return n;
    }
}
