using Adawat.KeyChanges;
using Xunit;

namespace Adawat.Tests;

public class KeyRemapperTests
{
    private const int LCtrl = 0xA2, LAlt = 0xA4, C = 0x43, Caps = 0x14, Tab = 0x09, F4 = 0x73;

    /// <summary>Records compare their lists by reference; compare what they do instead.</summary>
    private static void Same(KeyDecision expected, KeyDecision actual)
    {
        Assert.Equal(expected.Swallow, actual.Swallow);
        Assert.Equal(expected.Send, actual.Send);
    }

    private static KeyRemapper SwapCtrlAlt()
    {
        var r = new KeyRemapper();
        r.SetRules(new[] { (LAlt, LCtrl), (LCtrl, LAlt) });
        return r;
    }

    [Fact]
    public void SwappedKeysTurnIntoEachOther()
    {
        var r = SwapCtrlAlt();
        Same(KeyDecision.Replace(LCtrl, false), r.Process(LAlt, 0x38, up: false, sentByAdawat: false));
        Same(KeyDecision.Pass, r.Process(C, 0x2E, up: false, sentByAdawat: false));
        Same(KeyDecision.Pass, r.Process(C, 0x2E, up: true, sentByAdawat: false));
        Same(KeyDecision.Replace(LCtrl, true), r.Process(LAlt, 0x38, up: true, sentByAdawat: false));
        Same(KeyDecision.Replace(LAlt, false), r.Process(LCtrl, 0x1D, up: false, sentByAdawat: false));
    }

    [Fact]
    public void KeysAdawatSendsAreLeftAlone()
    {
        // Otherwise the swapped keys would bounce back and forth forever.
        var r = SwapCtrlAlt();
        Same(KeyDecision.Pass, r.Process(LCtrl, 0x1D, up: false, sentByAdawat: true));
    }

    [Fact]
    public void AltGrsHiddenCtrlIsNotChanged()
    {
        var r = SwapCtrlAlt();
        Same(KeyDecision.Pass, r.Process(LCtrl, 0x21D, up: false, sentByAdawat: false));
    }

    [Fact]
    public void ReleaseMatchesThePressEvenAfterTheRulesChange()
    {
        var r = SwapCtrlAlt();
        r.Process(LAlt, 0x38, up: false, sentByAdawat: false);
        r.SetRules(Array.Empty<(int, int)>());
        Same(KeyDecision.Replace(LCtrl, true), r.Process(LAlt, 0x38, up: true, sentByAdawat: false));
        // A key pressed before the rule existed is released normally.
        Same(KeyDecision.Pass, r.Process(LCtrl, 0x1D, up: true, sentByAdawat: false));
    }

    [Fact]
    public void HeldKeyRepeatsAsTheNewKey()
    {
        var r = SwapCtrlAlt();
        r.Process(LAlt, 0x38, up: false, sentByAdawat: false);
        r.Paused = true; // even if an excluded app comes to the front meanwhile
        Same(KeyDecision.Replace(LCtrl, false), r.Process(LAlt, 0x38, up: false, sentByAdawat: false));
    }

    [Fact]
    public void PausedPassesNewPresses()
    {
        var r = SwapCtrlAlt();
        r.Paused = true;
        Same(KeyDecision.Pass, r.Process(LAlt, 0x38, up: false, sentByAdawat: false));
    }

    [Fact]
    public void KeyCanBeTurnedOff()
    {
        var r = new KeyRemapper();
        r.SetRules(new[] { (Caps, 0) });
        Same(KeyDecision.Block, r.Process(Caps, 0x3A, up: false, sentByAdawat: false));
        Same(KeyDecision.Block, r.Process(Caps, 0x3A, up: true, sentByAdawat: false));
    }

    [Fact]
    public void ReleaseAllReturnsHeldTargets()
    {
        var r = SwapCtrlAlt();
        r.Process(LAlt, 0x38, up: false, sentByAdawat: false);
        Assert.Equal(new[] { LCtrl }, r.ReleaseAll());
        Assert.Empty(r.ReleaseAll());
        Same(KeyDecision.Pass, r.Process(LAlt, 0x38, up: true, sentByAdawat: false));
    }

    [Fact]
    public void InvalidRulesAreIgnored()
    {
        var r = new KeyRemapper();
        r.SetRules(new[] { (0, LCtrl), (LAlt, LAlt), (C, 0x1FF) });
        Assert.Equal(0, r.Count);
    }

    [Fact]
    public void AltTabStaysAltTabWhenAltWorksAsCtrl()
    {
        var r = new KeyRemapper();
        r.SetRules(new[] { new KeyRule(LAlt, LCtrl, new HashSet<int> { Tab, F4 }), new KeyRule(LCtrl, LAlt) });

        Same(KeyDecision.Replace(LCtrl, false), r.Process(LAlt, 0x38, up: false, sentByAdawat: false));
        // Tab while the Alt key is held: let go of Ctrl, press Alt, then Tab - in that order.
        var tab = r.Process(Tab, 0x0F, up: false, sentByAdawat: false);
        Assert.True(tab.Swallow);
        Assert.Equal(new[] { new KeyEvent(LCtrl, true), new KeyEvent(LAlt, false), new KeyEvent(Tab, false) }, tab.Send);
        // From now on it is a real Alt: more Tabs (cycling through windows) just pass.
        Same(KeyDecision.Pass, r.Process(Tab, 0x0F, up: true, sentByAdawat: false));
        Same(KeyDecision.Pass, r.Process(Tab, 0x0F, up: false, sentByAdawat: false));
        Same(KeyDecision.Replace(LAlt, false), r.Process(LAlt, 0x38, up: false, sentByAdawat: false)); // key repeat
        Same(KeyDecision.Replace(LAlt, true), r.Process(LAlt, 0x38, up: true, sentByAdawat: false));

        // Next time it starts as Ctrl again, and other keys (C) do not switch it.
        Same(KeyDecision.Replace(LCtrl, false), r.Process(LAlt, 0x38, up: false, sentByAdawat: false));
        Same(KeyDecision.Pass, r.Process(C, 0x2E, up: false, sentByAdawat: false));
        Same(KeyDecision.Replace(LCtrl, true), r.Process(LAlt, 0x38, up: true, sentByAdawat: false));
    }

    [Fact]
    public void KeepKeysDoNothingWhenTheKeyIsNotHeld()
    {
        var r = new KeyRemapper();
        r.SetRules(new[] { new KeyRule(LAlt, LCtrl, new HashSet<int> { Tab }) });
        Same(KeyDecision.Pass, r.Process(Tab, 0x0F, up: false, sentByAdawat: false));
    }
}
