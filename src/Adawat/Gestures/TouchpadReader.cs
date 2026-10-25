using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Adawat.Core;
using Adawat.Interop;
using static Adawat.Interop.Native;

namespace Adawat.Gestures;

/// <summary>
/// Reads finger positions from Windows Precision Touchpads through the Raw Input API
/// (HID usage page 0x0D "Digitizer", usage 0x05 "Touch Pad"), even while other apps have focus.
/// </summary>
public sealed class TouchpadReader : IDisposable
{
    private const ushort PageDigitizer = 0x0D, PageGeneric = 0x01;
    private const ushort UsageTouchPad = 0x05, UsageTipSwitch = 0x42, UsageContactId = 0x51, UsageContactCount = 0x54;
    private const ushort UsageX = 0x30, UsageY = 0x31;

    private sealed class FingerSlot
    {
        public ushort Collection;
        public int XMin, XMax, YMin, YMax;
        public bool HasId;
    }

    private sealed class Device
    {
        public IntPtr Preparsed;
        public int ContactCountCollection = -1;
        public List<FingerSlot> Slots = new();
        public double Aspect = 0.6; // touchpad height / width
        public bool Valid;
    }

    private readonly MessageWindow _window;
    private readonly Dictionary<IntPtr, Device> _devices = new();
    private readonly List<TouchContact> _pending = new();
    private readonly ushort[] _usages = new ushort[64];
    private IntPtr _buffer = IntPtr.Zero;
    private int _bufferSize;
    private byte[] _report = Array.Empty<byte>();
    private int _expected;
    private int _seen;
    private bool _registered;

    /// <summary>Raised on the UI thread with every complete frame (an empty list means all fingers lifted).</summary>
    public event Action<IReadOnlyList<TouchContact>>? Frame;

    public TouchpadReader(MessageWindow window)
    {
        _window = window;
        _window.Message += OnMessage;
    }

    /// <summary>True if a Precision Touchpad is connected.</summary>
    public static bool IsTouchpadPresent()
    {
        try
        {
            uint count = 0;
            uint size = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
            GetRawInputDeviceList(null, ref count, size);
            if (count == 0) return false;
            var list = new RAWINPUTDEVICELIST[count];
            if (GetRawInputDeviceList(list, ref count, size) == unchecked((uint)-1)) return false;

            foreach (var device in list.Where(d => d.dwType == RIM_TYPEHID))
            {
                uint infoSize = 0;
                GetRawInputDeviceInfo(device.hDevice, RIDI_DEVICEINFO, IntPtr.Zero, ref infoSize);
                if (infoSize < 32) continue;
                var buffer = Marshal.AllocHGlobal((int)infoSize);
                try
                {
                    Marshal.WriteInt32(buffer, (int)infoSize); // RID_DEVICE_INFO.cbSize
                    if (GetRawInputDeviceInfo(device.hDevice, RIDI_DEVICEINFO, buffer, ref infoSize) == unchecked((uint)-1)) continue;
                    // RID_DEVICE_INFO: cbSize, dwType, then RID_DEVICE_INFO_HID: vendor, product, version, usUsagePage, usUsage
                    ushort page = (ushort)Marshal.ReadInt16(buffer, 20);
                    ushort usage = (ushort)Marshal.ReadInt16(buffer, 22);
                    if (page == PageDigitizer && usage == UsageTouchPad) return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Touchpad detection failed", ex);
        }
        return false;
    }

    public bool Start()
    {
        if (_registered) return true;
        var device = new RAWINPUTDEVICE
        {
            usUsagePage = PageDigitizer,
            usUsage = UsageTouchPad,
            dwFlags = RIDEV_INPUTSINK,
            hwndTarget = _window.Handle,
        };
        _registered = RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        if (!_registered) Log.Error($"Touchpad registration failed: {Marshal.GetLastWin32Error()}");
        return _registered;
    }

    public void Stop()
    {
        if (!_registered) return;
        var device = new RAWINPUTDEVICE
        {
            usUsagePage = PageDigitizer,
            usUsage = UsageTouchPad,
            dwFlags = RIDEV_REMOVE,
            hwndTarget = IntPtr.Zero,
        };
        RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        _registered = false;
    }

    private void OnMessage(int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_INPUT || !_registered) return;
        try
        {
            ReadInput(lParam);
        }
        catch (Exception ex)
        {
            Log.Error("Touchpad input failed", ex);
        }
    }

    private void ReadInput(IntPtr lParam)
    {
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0) return;
        if (size > _bufferSize)
        {
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _bufferSize = (int)size;
            _buffer = Marshal.AllocHGlobal(_bufferSize);
        }
        if (GetRawInputData(lParam, RID_INPUT, _buffer, ref size, headerSize) != size) return;

        var header = Marshal.PtrToStructure<RAWINPUTHEADER>(_buffer);
        if (header.dwType != RIM_TYPEHID) return;

        var device = GetDevice(header.hDevice);
        if (device == null || !device.Valid) return;

        // RAWHID: dwSizeHid, dwCount, bRawData[dwSizeHid * dwCount]
        int reportSize = Marshal.ReadInt32(_buffer, (int)headerSize);
        int reportCount = Marshal.ReadInt32(_buffer, (int)headerSize + 4);
        if (reportSize <= 0 || reportCount <= 0) return;
        if (_report.Length != reportSize) _report = new byte[reportSize];

        for (int i = 0; i < reportCount; i++)
        {
            Marshal.Copy(_buffer + (int)headerSize + 8 + i * reportSize, _report, 0, reportSize);
            ParseReport(device, _report);
        }
    }

    private void ParseReport(Device device, byte[] report)
    {
        uint length = (uint)report.Length;
        uint count = 0;
        if (device.ContactCountCollection >= 0)
        {
            HidP_GetUsageValue(HidP_Input, PageDigitizer, (ushort)device.ContactCountCollection, UsageContactCount,
                out count, device.Preparsed, report, length);
        }
        else
        {
            count = (uint)device.Slots.Count;
        }

        // "Hybrid" touchpads split one frame over several reports; only the first carries the count.
        if (count > 0)
        {
            _expected = (int)count;
            _seen = 0;
            _pending.Clear();
        }
        if (_expected == 0) return;

        for (int i = 0; i < device.Slots.Count && _seen < _expected; i++)
        {
            var slot = device.Slots[i];
            _seen++;
            if (!IsTipDown(device, slot, report)) continue;

            if (HidP_GetUsageValue(HidP_Input, PageGeneric, slot.Collection, UsageX, out var x, device.Preparsed, report, length) != HIDP_STATUS_SUCCESS) continue;
            if (HidP_GetUsageValue(HidP_Input, PageGeneric, slot.Collection, UsageY, out var y, device.Preparsed, report, length) != HIDP_STATUS_SUCCESS) continue;

            int id = i;
            if (slot.HasId && HidP_GetUsageValue(HidP_Input, PageDigitizer, slot.Collection, UsageContactId, out var contactId, device.Preparsed, report, length) == HIDP_STATUS_SUCCESS)
            {
                id = (int)contactId;
            }

            double nx = (double)((int)x - slot.XMin) / Math.Max(1, slot.XMax - slot.XMin);
            double ny = (double)((int)y - slot.YMin) / Math.Max(1, slot.YMax - slot.YMin) * device.Aspect;
            _pending.Add(new TouchContact(id, nx, ny));
        }

        if (_seen >= _expected)
        {
            _expected = 0;
            Frame?.Invoke(_pending.ToArray());
        }
    }

    private bool IsTipDown(Device device, FingerSlot slot, byte[] report)
    {
        uint len = (uint)_usages.Length;
        int status = HidP_GetUsages(HidP_Input, PageDigitizer, slot.Collection, _usages, ref len, device.Preparsed, report, (uint)report.Length);
        if (status != HIDP_STATUS_SUCCESS) return false;
        for (int i = 0; i < len; i++)
        {
            if (_usages[i] == UsageTipSwitch) return true;
        }
        return false;
    }

    private Device? GetDevice(IntPtr handle)
    {
        if (_devices.TryGetValue(handle, out var known)) return known;

        var device = new Device();
        _devices[handle] = device;
        try
        {
            uint size = 0;
            GetRawInputDeviceInfo(handle, RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
            if (size == 0) return device;
            device.Preparsed = Marshal.AllocHGlobal((int)size);
            if (GetRawInputDeviceInfo(handle, RIDI_PREPARSEDDATA, device.Preparsed, ref size) == unchecked((uint)-1)) return device;

            if (HidP_GetCaps(device.Preparsed, out var caps) != HIDP_STATUS_SUCCESS) return device;
            ushort valueCount = caps.NumberInputValueCaps;
            if (valueCount == 0) return device;
            var values = new HIDP_VALUE_CAPS[valueCount];
            if (HidP_GetValueCaps(HidP_Input, values, ref valueCount, device.Preparsed) != HIDP_STATUS_SUCCESS) return device;

            var slots = new Dictionary<ushort, FingerSlot>();
            var hasX = new HashSet<ushort>();
            var hasY = new HashSet<ushort>();
            int physW = 0, physH = 0, logW = 0, logH = 0;

            for (int i = 0; i < valueCount; i++)
            {
                var v = values[i];
                ushort usage = v.UsageMin; // NotRange.Usage shares this offset
                if (!slots.TryGetValue(v.LinkCollection, out var slot))
                {
                    slots[v.LinkCollection] = slot = new FingerSlot { Collection = v.LinkCollection };
                }

                int logicalMax = v.LogicalMax > v.LogicalMin ? v.LogicalMax : (1 << Math.Min(30, (int)v.BitSize)) - 1;
                if (v.UsagePage == PageGeneric && usage == UsageX)
                {
                    slot.XMin = v.LogicalMin;
                    slot.XMax = logicalMax;
                    hasX.Add(v.LinkCollection);
                    physW = v.PhysicalMax - v.PhysicalMin;
                    logW = logicalMax - v.LogicalMin;
                }
                else if (v.UsagePage == PageGeneric && usage == UsageY)
                {
                    slot.YMin = v.LogicalMin;
                    slot.YMax = logicalMax;
                    hasY.Add(v.LinkCollection);
                    physH = v.PhysicalMax - v.PhysicalMin;
                    logH = logicalMax - v.LogicalMin;
                }
                else if (v.UsagePage == PageDigitizer && usage == UsageContactId)
                {
                    slot.HasId = true;
                }
                else if (v.UsagePage == PageDigitizer && usage == UsageContactCount)
                {
                    device.ContactCountCollection = v.LinkCollection;
                }
            }

            device.Slots = slots.Values
                .Where(s => hasX.Contains(s.Collection) && hasY.Contains(s.Collection))
                .OrderBy(s => s.Collection)
                .ToList();

            if (physW > 0 && physH > 0) device.Aspect = (double)physH / physW;
            else if (logW > 0 && logH > 0) device.Aspect = (double)logH / logW;
            device.Aspect = Math.Clamp(device.Aspect, 0.3, 1.2);

            device.Valid = device.Slots.Count > 0;
            Log.Info($"Touchpad found: {device.Slots.Count} finger slots, contact count {(device.ContactCountCollection >= 0 ? "yes" : "no")}, aspect {device.Aspect:F2}");
        }
        catch (Exception ex)
        {
            Log.Error("Could not read touchpad description", ex);
        }
        return device;
    }

    public void Dispose()
    {
        Stop();
        _window.Message -= OnMessage;
        foreach (var device in _devices.Values)
        {
            if (device.Preparsed != IntPtr.Zero) Marshal.FreeHGlobal(device.Preparsed);
        }
        _devices.Clear();
        if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
        _buffer = IntPtr.Zero;
    }
}
