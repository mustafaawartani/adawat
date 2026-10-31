using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Adawat.UI;

/// <summary>
/// WPF scrolls a fixed three lines for every wheel message, however small. A mouse wheel sends one
/// message per notch, but a touchpad sends many tiny ones, so two-finger scrolling in Adawat's
/// windows raced far too fast. This scrolls by the real amount instead: one notch (120) scrolls
/// the usual three lines, a touchpad's small steps scroll a little.
/// </summary>
public static class WheelScrolling
{
    private const double PixelsPerNotch = 48; // three 16-pixel lines, WPF's own amount for one notch
    private static readonly ConditionalWeakTable<ScrollViewer, StrongBox<double>> Rest = new();

    public static void Register()
    {
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewMouseWheel));
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0) return;
        // The outermost scroller hears it first: find the innermost one under the pointer that can
        // still move that way (a text box inside the page, a list inside a drop-down…).
        var target = Innermost(e.OriginalSource as DependencyObject, e.Delta);
        if (target == null) return;
        e.Handled = true;

        double lines = SystemParameters.WheelScrollLines > 0 ? SystemParameters.WheelScrollLines : 3;
        double notches = e.Delta / 120.0;
        if (target.CanContentScroll)
        {
            // Lists that scroll by items: count in items, keeping the part of an item not used yet.
            var rest = Rest.GetOrCreateValue(target);
            rest.Value += -notches * lines;
            int items = (int)Math.Truncate(rest.Value);
            if (items == 0) return;
            rest.Value -= items;
            target.ScrollToVerticalOffset(target.VerticalOffset + items);
        }
        else
        {
            target.ScrollToVerticalOffset(target.VerticalOffset - notches * PixelsPerNotch * lines / 3);
        }
    }

    private static ScrollViewer? Innermost(DependencyObject? source, int delta)
    {
        for (var node = source; node != null; node = Parent(node))
        {
            if (node is not ScrollViewer viewer || viewer.ScrollableHeight <= 0) continue;
            if (viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled) continue;
            bool canMove = delta > 0 ? viewer.VerticalOffset > 0 : viewer.VerticalOffset < viewer.ScrollableHeight;
            if (canMove) return viewer;
        }
        return null;
    }

    private static DependencyObject? Parent(DependencyObject node) =>
        // Visual parents only: a drop-down list must not scroll the page behind its popup.
        node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
}
