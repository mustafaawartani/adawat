using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static Adawat.Interop.Native;

namespace Adawat.Interop;

/// <summary>Synthesizes keyboard and mouse input with SendInput.</summary>
public static class InputSender
{
    /// <summary>Marks input we inject, so our own hooks can ignore it ("ADAW").</summary>
    public static readonly IntPtr Signature = new(0x41444157);

    public const ushort VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    public const ushort VK_LEFT = 0x25, VK_RIGHT = 0x27, VK_C = 0x43;
    public const ushort VK_OEM_PLUS = 0xBB, VK_OEM_MINUS = 0xBD;
    public const ushort VK_BROWSER_BACK = 0xA6, VK_BROWSER_FORWARD = 0xA7;
    public const ushort VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5;

    /// <summary>An unassigned virtual key. Tapping it while Alt is held stops apps from opening their menu bar when Alt is released.</summary>
    public const ushort VK_MENU_MASK = 0xE8;

    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    private static readonly HashSet<ushort> ExtendedKeys = new()
    {
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, // PgUp..Down, Insert, Delete
        VK_RCONTROL, VK_RMENU, VK_LWIN, VK_RWIN, 0x6F, 0x90,
        VK_BROWSER_BACK, VK_BROWSER_FORWARD, 0xA8, 0xA9, 0xAA, 0xAB, 0xAC,
    };

    private static INPUT Key(ushort vk, bool up)
    {
        uint flags = up ? KEYEVENTF_KEYUP : 0;
        if (ExtendedKeys.Contains(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = (ushort)MapVirtualKey(vk, 0),
                    dwFlags = flags,
                    dwExtraInfo = Signature,
                },
            },
        };
    }

    private static INPUT Mouse(uint flags, uint data) => new()
    {
        type = INPUT_MOUSE,
        U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = data, dwExtraInfo = Signature } },
    };

    private static void Send(params INPUT[] inputs) => SendInput((uint)inputs.Length, inputs, InputSize);

    public static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public static bool AnyModifierDown() =>
        IsDown(VK_SHIFT) || IsDown(VK_CONTROL) || IsDown(VK_MENU) || IsDown(VK_LWIN) || IsDown(VK_RWIN);

    /// <summary>Taps the mask key so a held Alt does not activate the target app's menu bar.</summary>
    public static void MaskMenuKey() => Send(Key(VK_MENU_MASK, false), Key(VK_MENU_MASK, true));

    /// <summary>Sends key-up for modifier keys that are still physically held.</summary>
    public static void ReleaseModifiers()
    {
        var inputs = new List<INPUT>();
        foreach (var vk in new[] { VK_LMENU, VK_RMENU, VK_LCONTROL, VK_RCONTROL, VK_LSHIFT, VK_RSHIFT, VK_LWIN, VK_RWIN })
        {
            if (IsDown(vk)) inputs.Add(Key(vk, true));
        }
        if (inputs.Count > 0)
        {
            // Prevent a lone Alt/Win release from opening a menu or the Start menu.
            inputs.Insert(0, Key(VK_MENU_MASK, true));
            inputs.Insert(0, Key(VK_MENU_MASK, false));
            Send(inputs.ToArray());
        }
    }

    public static void Chord(ushort modifier, ushort key) =>
        Send(Key(modifier, false), Key(key, false), Key(key, true), Key(modifier, true));

    public static void Tap(ushort key) => Send(Key(key, false), Key(key, true));

    public static void CopyShortcut() => Chord(VK_CONTROL, VK_C);

    public static void MouseXButton(bool forward)
    {
        uint button = forward ? XBUTTON2 : XBUTTON1;
        Send(Mouse(MOUSEEVENTF_XDOWN, button), Mouse(MOUSEEVENTF_XUP, button));
    }

    public static void CtrlWheel(int delta) =>
        Send(Key(VK_CONTROL, false), Mouse(MOUSEEVENTF_WHEEL, unchecked((uint)delta)), Key(VK_CONTROL, true));
}
