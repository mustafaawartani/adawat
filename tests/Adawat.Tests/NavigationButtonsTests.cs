using Adawat.Gestures;
using Xunit;

namespace Adawat.Tests;

public class NavigationButtonsTests
{
    private static SeenButton B(string name, bool enabled, string id = "") => new(name, id, enabled);

    [Fact]
    public void GreyedOutBackButtonMeansNowhereToGo()
    {
        var buttons = new[] { B("New chat", true), B("Go back", false), B("Go forward", false) };
        Assert.Equal(NavigationState.NotPossible, NavigationButtons.Classify(buttons, forward: false));
        Assert.Equal(NavigationState.NotPossible, NavigationButtons.Classify(buttons, forward: true));
    }

    [Fact]
    public void UsableBackButtonAllowsTheSwipe()
    {
        // File Explorer names its button after the folder it goes back to.
        var buttons = new[] { B("Back to Downloads (Alt + Left Arrow)", true), B("Forward (Alt + Right Arrow)", false) };
        Assert.Equal(NavigationState.Possible, NavigationButtons.Classify(buttons, forward: false));
        Assert.Equal(NavigationState.NotPossible, NavigationButtons.Classify(buttons, forward: true));
    }

    [Fact]
    public void WinUiBackButtonIsFoundById()
    {
        var buttons = new[] { B("", false, "NavigationViewBackButton") };
        Assert.Equal(NavigationState.NotPossible, NavigationButtons.Classify(buttons, forward: false));
    }

    [Fact]
    public void NoBackButtonIsUnknown()
    {
        var buttons = new[] { B("Background color", false), B("Backup now", false), B("Back to top", false), B("Send", true) };
        Assert.Equal(NavigationState.Unknown, NavigationButtons.Classify(buttons, forward: false));
    }

    [Fact]
    public void ArabicAndSpanishNamesAreRecognized()
    {
        Assert.True(NavigationButtons.IsBack(B("رجوع", true)));
        Assert.True(NavigationButtons.IsBack(B("Atrás", true)));
        Assert.True(NavigationButtons.IsForward(B("Adelante", true)));
    }
}
