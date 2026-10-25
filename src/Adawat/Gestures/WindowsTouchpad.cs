using System;
using System.Runtime.InteropServices;
using Adawat.Core;
using Microsoft.Win32;

namespace Adawat.Gestures;

/// <summary>
/// Reads and changes the Windows "Pinch to zoom" touchpad setting
/// (Settings → Bluetooth &amp; devices → Touchpad → Scroll &amp; zoom).
/// When it is on, Windows turns pinches into Ctrl + mouse wheel, which makes
/// File Explorer change icon size and other apps change font size.
/// </summary>
public static class WindowsTouchpad
{
    private const uint SPI_GETTOUCHPADPARAMETERS = 0x00AE;
    private const uint SPI_SETTOUCHPADPARAMETERS = 0x00AF;
    private const uint SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;
    private const int ZoomBit = 1 << 8;          // zoomEnabled in the second flags word
    private const int FlagsOffset = 16;          // versionNumber, maxSupportedContacts, legacyFeatures, flags1, flags2
    private const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, IntPtr data, uint winIni);

    /// <summary>The size of TOUCHPAD_PARAMETERS this Windows accepts, or 0 if the API is missing (older Windows).</summary>
    private static uint _size = uint.MaxValue;

    private static uint FindSize()
    {
        if (_size != uint.MaxValue) return _size;
        _size = 0;
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            for (uint size = 28; size <= 128; size += 4)
            {
                Clear(buffer, 256);
                Marshal.WriteInt32(buffer, 0, 1); // TOUCHPAD_PARAMETERS_VERSION_1
                if (SystemParametersInfo(SPI_GETTOUCHPADPARAMETERS, size, buffer, 0))
                {
                    _size = size;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Info($"Touchpad settings API unavailable: {ex.Message}");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return _size;
    }

    /// <summary>
    /// Sanity check of the layout before writing anything back: a touchpad is present and the
    /// reserved bits of both flag words are zero. If not, we leave the setting alone.
    /// </summary>
    private static bool LooksValid(IntPtr buffer)
    {
        int flags1 = Marshal.ReadInt32(buffer, 12);
        int flags2 = Marshal.ReadInt32(buffer, FlagsOffset);
        bool touchpadPresent = (flags1 & 1) != 0;
        bool reserved1Clear = (flags1 & ~0x7F) == 0;   // 7 defined bits
        bool reserved2Clear = (flags2 & ~0x3FF) == 0;  // 10 defined bits
        return touchpadPresent && reserved1Clear && reserved2Clear;
    }

    private static void Clear(IntPtr buffer, int length)
    {
        for (int i = 0; i < length; i += 4) Marshal.WriteInt32(buffer, i, 0);
    }

    /// <summary>True/false if Windows' pinch zoom is on/off, null if it cannot be read.</summary>
    public static bool? IsPinchZoomEnabled()
    {
        uint size = FindSize();
        if (size > 0)
        {
            var buffer = Marshal.AllocHGlobal(256);
            try
            {
                Clear(buffer, 256);
                Marshal.WriteInt32(buffer, 0, 1);
                if (SystemParametersInfo(SPI_GETTOUCHPADPARAMETERS, size, buffer, 0) && LooksValid(buffer))
                    return (Marshal.ReadInt32(buffer, FlagsOffset) & ZoomBit) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        try
        {
            var value = Registry.GetValue($@"HKEY_CURRENT_USER\{RegistryKey}", "ZoomEnabled", null);
            if (value is int i) return i != 0;
        }
        catch
        {
        }
        return null;
    }

    /// <summary>
    /// Turns Windows' pinch zoom on or off. Returns true if the change took effect right away;
    /// false means it was only saved and the user should flip it in Settings (or sign out and in).
    /// </summary>
    public static bool SetPinchZoom(bool enabled)
    {
        bool applied = false;
        uint size = FindSize();
        if (size > 0)
        {
            var buffer = Marshal.AllocHGlobal(256);
            try
            {
                Clear(buffer, 256);
                Marshal.WriteInt32(buffer, 0, 1);
                if (SystemParametersInfo(SPI_GETTOUCHPADPARAMETERS, size, buffer, 0) && LooksValid(buffer))
                {
                    int flags = Marshal.ReadInt32(buffer, FlagsOffset);
                    flags = enabled ? flags | ZoomBit : flags & ~ZoomBit;
                    Marshal.WriteInt32(buffer, FlagsOffset, flags);
                    applied = SystemParametersInfo(SPI_SETTOUCHPADPARAMETERS, size, buffer, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE)
                              && IsPinchZoomEnabled() == enabled;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not change the Windows pinch setting", ex);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        if (!applied)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKey);
                key.SetValue("ZoomEnabled", enabled ? unchecked((int)0xFFFFFFFF) : 0, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                Log.Error("Could not save the Windows pinch setting", ex);
            }
        }
        Log.Info($"Windows pinch zoom {(enabled ? "on" : "off")} ({(applied ? "applied" : "saved for next sign-in")})");
        return applied;
    }
}
