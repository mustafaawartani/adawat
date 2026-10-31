using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Adawat.Gestures;

/// <summary>Whether the app under the pointer can go back (or forward) right now.</summary>
public enum NavigationState
{
    /// <summary>No back/forward button was found: the app may still react to the mouse back button.</summary>
    Unknown,
    /// <summary>A back/forward button is there and can be clicked.</summary>
    Possible,
    /// <summary>The app's back/forward button is there but greyed out: there is nowhere to go.</summary>
    NotPossible,
}

/// <summary>One button seen through Windows accessibility.</summary>
public readonly record struct SeenButton(string Name, string AutomationId, bool Enabled);

/// <summary>
/// Recognizes back and forward buttons by their accessible name or id (File Explorer, Settings,
/// WinUI apps, Electron apps such as Claude or VS Code, Office…). Pure logic so it can be tested.
/// </summary>
public static class NavigationButtons
{
    private static readonly Regex BackName = new(
        @"^(go |navigate )?(back|previous page)(\b|$)|^(رجوع|السابق|الخلف|عودة)|^(atrás|atras|volver|retroceder)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ForwardName = new(
        @"^(go |navigate )?(forward|next page)(\b|$)|^(تقدم|التالي|الأمام|للأمام)|^(adelante|avanzar)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BackId = new(@"(^|[^a-z])back(button|btn)?$|^NavigationViewBackButton$|BackButton", RegexOptions.IgnoreCase);
    private static readonly Regex ForwardId = new(@"(^|[^a-z])forward(button|btn)?$|ForwardButton", RegexOptions.IgnoreCase);

    public static bool IsBack(SeenButton b) =>
        (BackName.IsMatch(b.Name.Trim()) && !b.Name.Contains("top", StringComparison.OrdinalIgnoreCase)) || BackId.IsMatch(b.AutomationId);

    public static bool IsForward(SeenButton b) => ForwardName.IsMatch(b.Name.Trim()) || ForwardId.IsMatch(b.AutomationId);

    /// <summary>
    /// Any usable back (forward) button: possible. Only greyed-out ones: not possible. None: unknown,
    /// so apps without a visible button keep working as before.
    /// </summary>
    public static NavigationState Classify(IEnumerable<SeenButton> buttons, bool forward)
    {
        bool sawDisabled = false;
        foreach (var b in buttons)
        {
            if (!(forward ? IsForward(b) : IsBack(b))) continue;
            if (b.Enabled) return NavigationState.Possible;
            sawDisabled = true;
        }
        return sawDisabled ? NavigationState.NotPossible : NavigationState.Unknown;
    }
}
