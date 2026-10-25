using Adawat.Core;
using Xunit;

namespace Adawat.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Alt+1", HotkeyModifiers.Alt, 0x31)]
    [InlineData("Alt+2", HotkeyModifiers.Alt, 0x32)]
    [InlineData("ctrl+shift+t", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 0x54)]
    [InlineData("Win+F5", HotkeyModifiers.Win, 0x74)]
    [InlineData("F8", HotkeyModifiers.None, 0x77)]
    [InlineData("Ctrl+Alt+Space", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x20)]
    [InlineData("Ctrl+NumPlus", HotkeyModifiers.Ctrl, 0x6B)]
    public void Parses(string text, HotkeyModifiers mods, int vk)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(mods, hotkey.Modifiers);
        Assert.Equal(vk, hotkey.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Alt")]
    [InlineData("Alt+Banana")]
    [InlineData("Alt+1+2")]
    public void RejectsInvalid(string text) => Assert.False(Hotkey.TryParse(text, out _));

    [Theory]
    [InlineData("Alt+1")]
    [InlineData("Ctrl+Alt+Shift+Win+F12")]
    [InlineData("Shift+Delete")]
    [InlineData("Ctrl+=")]
    public void RoundTrips(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(text, hotkey.ToString());
    }
}
