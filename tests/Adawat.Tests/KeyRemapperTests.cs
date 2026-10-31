using Adawat.KeyChanges;
using Xunit;

namespace Adawat.Tests;

public class KeyRemapperTests
{
    private const int LCtrl = 0xA2, LAlt = 0xA4, C = 0x43, Caps = 0x14;

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
        Assert.Equal(new KeyDecision(true, LCtrl), r.Process(LAlt, 0x38, up: false, sentByAdawat: false));
        Assert.Equal(KeyDecision.Pass, r.Process(C, 0x2E, up: false, sentByAdawat: false));
        Assert.Equal(KeyDecision.Pass, r.Process(C, 0x2E, up: true, sentByAdawat: false));
        Assert.Equal(new KeyDecision(true, LCtrl), r.Process(LAlt, 0x38, up: true, sentByAdawat: false));
        Assert.Equal(new KeyDecision(true, LAlt), r.Process(LCtrl, 0x1D, up: false, sentByAdawat: false));
    }

    [Fact]
    public void KeysAdawatSendsAreLeftAlone()
    {
        // Otherwise the swapped keys would bounce back and forth forever.
        var r = SwapCtrlAlt();
        Assert.Equal(KeyDecision.Pass, r.Process(LCtrl, 0x1D, up: false, sentByAdawat: true));
    }

    [Fact]
    public void AltGrsHiddenCtrlIsNotChanged()
    {
        var r = SwapCtrlAlt();
        Assert.Equal(KeyDecision.Pass, r.Process(LCtrl, 0x21D, up: false, sentByAdawat: false));
    }

    [Fact]
    public void ReleaseMatchesThePressEvenAfterTheRulesChange()
    {
        var r = SwapCtrlAlt();
        r.Process(LAlt, 0x38, up: false, sentByAdawat: false);
        r.SetRules(Array.Empty<(int, int)>());
        Assert.Equal(new KeyDecision(true, LCtrl), r.Process(LAlt, 0x38, up: true, sentByAdawat: false));
        // A key pressed before the rule existed is released normally.
        Assert.Equal(KeyDecision.Pass, r.Process(LCtrl, 0x1D, up: true, sentByAdawat: false));
    }

    [Fact]
    public void HeldKeyRepeatsAsTheNewKey()
    {
        var r = SwapCtrlAlt();
        r.Process(LAlt, 0x38, up: false, sentByAdawat: false);
        r.Paused = true; // even if an excluded app comes to the front meanwhile
        Assert.Equal(new KeyDecision(true, LCtrl), r.Process(LAlt, 0x38, up: false, sentByAdawat: false));
    }

    [Fact]
    public void PausedPassesNewPresses()
    {
        var r = SwapCtrlAlt();
        r.Paused = true;
        Assert.Equal(KeyDecision.Pass, r.Process(LAlt, 0x38, up: false, sentByAdawat: false));
    }

    [Fact]
    public void KeyCanBeTurnedOff()
    {
        var r = new KeyRemapper();
        r.SetRules(new[] { (Caps, 0) });
        Assert.Equal(KeyDecision.Block, r.Process(Caps, 0x3A, up: false, sentByAdawat: false));
        Assert.Equal(KeyDecision.Block, r.Process(Caps, 0x3A, up: true, sentByAdawat: false));
    }

    [Fact]
    public void ReleaseAllReturnsHeldTargets()
    {
        var r = SwapCtrlAlt();
        r.Process(LAlt, 0x38, up: false, sentByAdawat: false);
        Assert.Equal(new[] { LCtrl }, r.ReleaseAll());
        Assert.Empty(r.ReleaseAll());
        Assert.Equal(KeyDecision.Pass, r.Process(LAlt, 0x38, up: true, sentByAdawat: false));
    }

    [Fact]
    public void InvalidRulesAreIgnored()
    {
        var r = new KeyRemapper();
        r.SetRules(new[] { (0, LCtrl), (LAlt, LAlt), (C, 0x1FF) });
        Assert.Equal(0, r.Count);
    }
}
