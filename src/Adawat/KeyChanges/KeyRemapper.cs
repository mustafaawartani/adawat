using System.Collections.Generic;

namespace Adawat.KeyChanges;

/// <summary>What to do with one key press or release.</summary>
/// <param name="Swallow">Keep the original key from reaching apps.</param>
/// <param name="SendKey">The key to send instead (0 = nothing).</param>
public readonly record struct KeyDecision(bool Swallow, int SendKey)
{
    public static readonly KeyDecision Pass = new(false, 0);
    public static readonly KeyDecision Block = new(true, 0);
}

/// <summary>
/// The rules behind "make one key act as another", kept free of Windows calls so they can be tested.
/// Keys are Windows virtual-key codes with sides (left Ctrl is 0xA2, right Ctrl 0xA3…), as the
/// keyboard hook reports them. A target of 0 turns the key off.
/// </summary>
public sealed class KeyRemapper
{
    public const int VK_LCONTROL = 0xA2;
    /// <summary>The left Ctrl that Windows adds in front of AltGr on many keyboard layouts.</summary>
    private const int AltGrFakeCtrlScanCode = 0x21D;

    private readonly object _gate = new();
    private Dictionary<int, int> _map = new();
    // Keys held down right now and what they were turned into, so the release always matches the
    // press even if the rules or the app in front changed in between.
    private readonly Dictionary<int, int> _held = new();

    /// <summary>True while an app in the "keys are not changed" list is in front: new presses pass through.</summary>
    public bool Paused { get; set; }

    public int Count
    {
        get { lock (_gate) return _map.Count; }
    }

    public void SetRules(IEnumerable<(int From, int To)> rules)
    {
        var map = new Dictionary<int, int>();
        foreach (var (from, to) in rules)
        {
            if (from <= 0 || from > 0xFE || to < 0 || to > 0xFE || from == to) continue;
            map[from] = to;
        }
        lock (_gate) _map = map;
    }

    public KeyDecision Process(int vk, int scanCode, bool up, bool sentByAdawat)
    {
        if (sentByAdawat) return KeyDecision.Pass;
        if (vk == VK_LCONTROL && scanCode == AltGrFakeCtrlScanCode) return KeyDecision.Pass;
        lock (_gate)
        {
            if (up)
            {
                if (!_held.Remove(vk, out var target)) return KeyDecision.Pass;
                return new KeyDecision(true, target);
            }
            if (_held.TryGetValue(vk, out var repeating)) return new KeyDecision(true, repeating); // key repeat
            if (Paused || !_map.TryGetValue(vk, out var to)) return KeyDecision.Pass;
            _held[vk] = to;
            return new KeyDecision(true, to);
        }
    }

    /// <summary>Forgets the held keys and returns the keys Adawat pressed for them, so they can be released.</summary>
    public List<int> ReleaseAll()
    {
        lock (_gate)
        {
            var keys = new List<int>();
            foreach (var target in _held.Values)
            {
                if (target != 0 && !keys.Contains(target)) keys.Add(target);
            }
            _held.Clear();
            return keys;
        }
    }
}
