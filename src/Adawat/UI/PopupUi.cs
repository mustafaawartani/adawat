using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Adawat.UI;

/// <summary>
/// Building blocks for the light, quiet look of the translator popup and the picture preview.
/// Everything is colored with the "Adawat.*" palette brushes (see <see cref="ThemeHelper.ApplyPalette"/>),
/// so a theme or accent change only has to refresh the window's resources.
/// </summary>
public static class PopupUi
{
    public static WpfTextBlock Text(string text, double size, string brush = "Text", FontWeight? weight = null)
    {
        var block = new WpfTextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = weight ?? FontWeights.Normal };
        block.SetResourceReference(WpfTextBlock.ForegroundProperty, "Adawat." + brush);
        return block;
    }

    public static SymbolIcon Icon(SymbolRegular symbol, double size, string brush)
    {
        var icon = new SymbolIcon { Symbol = symbol, FontSize = size };
        icon.SetResourceReference(Control.ForegroundProperty, "Adawat." + brush);
        return icon;
    }

    /// <summary>A small round-cornered icon button that only shows a background on hover.</summary>
    public static Border IconButton(SymbolRegular symbol, string tooltip, Action onClick, double size = 16)
    {
        var icon = Icon(symbol, size, "Muted");
        var button = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Child = icon,
            ToolTip = tooltip,
            Cursor = Cursors.Hand,
        };
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        MakeClickable(button, onClick, hoverBrush: "Hover");
        button.MouseEnter += (_, _) => icon.SetResourceReference(Control.ForegroundProperty, IsActive(button) ? "Adawat.Accent" : "Adawat.Text");
        button.MouseLeave += (_, _) => icon.SetResourceReference(Control.ForegroundProperty, IsActive(button) ? "Adawat.Accent" : "Adawat.Muted");
        return button;
    }

    /// <summary>Shows an icon button as switched on (pinned) or off.</summary>
    public static void SetActive(Border button, bool active)
    {
        button.Tag = active ? "active" : null;
        if (button.Child is SymbolIcon icon) icon.SetResourceReference(Control.ForegroundProperty, active ? "Adawat.Accent" : "Adawat.Muted");
        if (active) button.SetResourceReference(Border.BackgroundProperty, "Adawat.AccentSoft");
        else button.Background = Brushes.Transparent;
    }

    private static bool IsActive(Border button) => button.Tag as string == "active";

    /// <summary>The one filled button: accent background, rounded, short label.</summary>
    public static Border PillButton(string text, Action onClick)
    {
        var label = Text(text, 13, "OnAccent", FontWeights.SemiBold);
        label.TextWrapping = TextWrapping.NoWrap;
        var button = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 6, 14, 7),
            Child = label,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.SetResourceReference(Border.BackgroundProperty, "Adawat.Accent");
        button.MouseEnter += (_, _) => button.Opacity = 0.9;
        button.MouseLeave += (_, _) => button.Opacity = 1;
        MakeClickable(button, onClick, hoverBrush: null);
        return button;
    }

    /// <summary>Text that looks like a quiet link and turns accent-colored on hover.</summary>
    public static WpfTextBlock Link(string text, Action onClick)
    {
        var link = Text(text, 12, "Muted");
        link.TextWrapping = TextWrapping.NoWrap;
        link.Cursor = Cursors.Hand;
        link.VerticalAlignment = VerticalAlignment.Center;
        link.MouseEnter += (_, _) =>
        {
            link.SetResourceReference(WpfTextBlock.ForegroundProperty, "Adawat.Accent");
            link.TextDecorations = TextDecorations.Underline;
        };
        link.MouseLeave += (_, _) =>
        {
            link.SetResourceReference(WpfTextBlock.ForegroundProperty, "Adawat.Muted");
            link.TextDecorations = null;
        };
        link.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };
        return link;
    }

    /// <summary>
    /// Click on release (like a real button), a hover background, and a slight press effect.
    /// </summary>
    public static void MakeClickable(Border element, Action onClick, string? hoverBrush)
    {
        bool pressed = false;
        element.MouseEnter += (_, _) =>
        {
            if (hoverBrush != null && !IsActive(element)) element.SetResourceReference(Border.BackgroundProperty, "Adawat." + hoverBrush);
        };
        element.MouseLeave += (_, _) =>
        {
            pressed = false;
            element.RenderTransform = null;
            if (hoverBrush != null && !IsActive(element)) element.Background = Brushes.Transparent;
        };
        element.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            pressed = true;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = new ScaleTransform(0.96, 0.96);
        };
        element.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            element.RenderTransform = null;
            if (!pressed) return;
            pressed = false;
            onClick();
        };
    }

    /// <summary>A borderless text box (the frame is drawn by the caller), editable or read-only.</summary>
    public static WpfTextBox PlainTextBox(double size, bool readOnly)
    {
        var template = new ControlTemplate(typeof(WpfTextBox))
        {
            VisualTree = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost"),
        };
        var box = new WpfTextBox
        {
            Template = template,
            IsReadOnly = readOnly,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = size,
            Padding = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsUndoEnabled = !readOnly,
            FocusVisualStyle = null,
        };
        box.SetResourceReference(Control.ForegroundProperty, "Adawat.Text");
        box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.SelectionBrushProperty, "Adawat.Accent");
        box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "Adawat.Accent");
        return box;
    }

    /// <summary>A rounded panel in the soft card color.</summary>
    public static Border Card(UIElement child, Thickness padding)
    {
        var card = new Border { CornerRadius = new CornerRadius(12), Padding = padding, Child = child };
        card.SetResourceReference(Border.BackgroundProperty, "Adawat.Subtle");
        return card;
    }
}
