using System;
using System.Collections.Generic;

namespace Adawat.KeyChanges;

/// <summary>One key press or release to send.</summary>
public readonly record struct KeyEvent(int Key, bool Up);

/// <summary>What to do with one key press or release: let it through, or swallow it and send these keys instead.</summary>
public sealed record KeyDecision(bool Swallow, IReadOnlyList<KeyEvent> Send)
{
    public static readonly KeyDecision Pass = new(false, Array.Empty<KeyEvent>());
    public static readonly KeyDecision Block = new(true, Array.Empty<KeyEvent>());

    public static KeyDecision Replace(int key, bool up) => key == 0 ? Block : new(true, new[] { new KeyEvent(key, up) });
}

/// <summary>
/// "<see cref="From"/> works as <see cref="To"/>" (0 = the key does nothing). While From is held,
/// pressing one of the <see cref="Keep"/> keys makes it its old self again until it is released, so
/// with Left Alt working as Left Ctrl, Alt + Tab can still switch windows.
/// </summary>
public readonly record struct KeyRule(int From, int To, ISet<int>? Keep = null);

/// <summary>
/// The rules behind "make one key act as another", kept free of Windows calls so they can be tested.
/// Keys are Windows virtual-key codes with sides (left Ctrl is 0xA2, right Ctrl 0xA3…), as the
/// keyboard hook reports them.
/// </summary>
public sealed class KeyRemapper
{
    public const int VK_LCONTROL = 0xA2;
    public const int VK_RMENU = 0xA5;
    /// <summary>The left Ctrl that Windows adds in front of AltGr on many keyboard layouts.</summary>
    private const int AltGrFakeCtrlScanCode = 0x21D;

    private readonly object _gate = new();
    private Dictionary<int, KeyRule> _rules = new();
    // Keys held down right now and what is being sent for them, so the release always matches the
    // press even if the rules or the app in front changed in between.
    private readonly Dictionary<int, int> _held = new();
    private bool _swallowFakeCtrl;

    /// <summary>True while an app in the "keys are not changed" list is in front: new presses pass through.</summary>
    public bool Paused { get; set; }

    public int Count
    {
        get { lock (_gate) return _rules.Count; }
    }

    public void SetRules(IEnumerable<(int From, int To)> rules)
    {
        var list = new List<KeyRule>();
        foreach (var (from, to) in rules) list.Add(new KeyRule(from, to));
        SetRules(list);
    }

    public void SetRules(IEnumerable<KeyRule> rules)
    {
        var map = new Dictionary<int, KeyRule>();
        foreach (var rule in rules)
        {
            if (rule.From <= 0 || rule.From > 0xFE || rule.To < 0 || rule.To > 0xFE || rule.From == rule.To) continue;
            map[rule.From] = rule;
        }
        lock (_gate) _rules = map;
    }

    public KeyDecision Process(int vk, int scanCode, bool up, bool sentByAdawat)
    {
        if (sentByAdawat) return KeyDecision.Pass;
        lock (_gate)
        {
            // Keyboard layouts with AltGr (Spanish, French, German…) send a hidden Left Ctrl with
            // every Right Alt press, so Right Alt works as Ctrl + Alt there but as a plain Alt with
            // the English layout. The hidden Ctrl is never changed itself; when Right Alt has its own
            // change it is dropped, so Right Alt acts the same with every layout.
            if (vk == VK_LCONTROL && scanCode == AltGrFakeCtrlScanCode)
            {
                if (!up) _swallowFakeCtrl = !Paused && _rules.ContainsKey(VK_RMENU);
                var swallow = _swallowFakeCtrl;
                if (up) _swallowFakeCtrl = false;
                return swallow ? KeyDecision.Block : KeyDecision.Pass;
            }
            if (up)
            {
                return _held.Remove(vk, out var sent) ? KeyDecision.Replace(sent, up: true) : KeyDecision.Pass;
            }
            if (_held.TryGetValue(vk, out var repeating)) return KeyDecision.Replace(repeating, up: false); // key repeat

            var keep = KeepSwitch(vk);
            if (keep != null) return keep;

            if (Paused || !_rules.TryGetValue(vk, out var rule)) return KeyDecision.Pass;
            _held[vk] = rule.To;
            return KeyDecision.Replace(rule.To, up: false);
        }
    }

    /// <summary>
    /// A "keep" key pressed while a changed key is held: let go of the new key, press the old one,
    /// then this key, all in that order (sent together, so the app sees Alt before Tab).
    /// </summary>
    private KeyDecision? KeepSwitch(int vk)
    {
        List<KeyEvent>? events = null;
        foreach (var (source, sent) in new List<KeyValuePair<int, int>>(_held))
        {
            if (!_rules.TryGetValue(source, out var rule) || sent != rule.To || rule.Keep == null || !rule.Keep.Contains(vk)) continue;
            events ??= new List<KeyEvent>();
            if (rule.To != 0) events.Add(new KeyEvent(rule.To, Up: true));
            events.Add(new KeyEvent(source, Up: false));
            _held[source] = source;
        }
        if (events == null) return null;
        events.Add(new KeyEvent(vk, Up: false));
        return new KeyDecision(true, events);
    }

    /// <summary>Forgets the held keys and returns the keys Adawat pressed for them, so they can be released.</summary>
    public List<int> ReleaseAll()
    {
        lock (_gate)
        {
            var keys = new List<int>();
            foreach (var sent in _held.Values)
            {
                if (sent != 0 && !keys.Contains(sent)) keys.Add(sent);
            }
            _held.Clear();
            _swallowFakeCtrl = false;
            return keys;
        }
    }
}
