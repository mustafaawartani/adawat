using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using WpfButton = Wpf.Ui.Controls.Button;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace Adawat.UI;

/// <summary>Small factory helpers for building the UI in code.</summary>
public static class Ui
{
    public const string FontFamilyName = "Segoe UI Variable Text, Segoe UI";

    public static SymbolIcon Icon(SymbolRegular symbol, double size = 16) => new() { Symbol = symbol, FontSize = size };

    public static WpfButton IconButton(SymbolRegular symbol, string tooltip, Action onClick, double size = 16)
    {
        var button = new WpfButton
        {
            Icon = Icon(symbol, size),
            Appearance = ControlAppearance.Transparent,
            ToolTip = tooltip,
            Padding = new Thickness(6),
            MinWidth = 0,
            MinHeight = 0,
            BorderThickness = new Thickness(0),
            Focusable = false,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    public static WpfButton Button(string text, Action onClick, bool primary = false, SymbolRegular? icon = null)
    {
        var button = new WpfButton
        {
            Content = text,
            Appearance = primary ? ControlAppearance.Primary : ControlAppearance.Secondary,
        };
        if (icon != null) button.Icon = Icon(icon.Value);
        button.Click += (_, _) => onClick();
        return button;
    }

    public static WpfTextBlock Text(string text, double size = 14, bool secondary = false, FontWeight? weight = null)
    {
        var block = new WpfTextBlock
        {
            Text = text,
            FontSize = size,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = weight ?? FontWeights.Normal,
        };
        block.SetResourceReference(WpfTextBlock.ForegroundProperty, secondary ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
        return block;
    }

    public static Border Divider(double margin = 8)
    {
        var border = new Border { Height = 1, Margin = new Thickness(0, margin, 0, margin) };
        border.SetResourceReference(Border.BackgroundProperty, "DividerStrokeColorDefaultBrush");
        return border;
    }

    /// <summary>A borderless, read-only text box: selectable text that looks like plain text.</summary>
    public static System.Windows.Controls.TextBox SelectableText(double size)
    {
        var template = new ControlTemplate(typeof(System.Windows.Controls.TextBox));
        template.VisualTree = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        var box = new System.Windows.Controls.TextBox
        {
            Template = template,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = size,
            Padding = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsUndoEnabled = false,
            FocusVisualStyle = null,
        };
        box.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.SelectionBrushProperty, "AccentFillColorDefaultBrush");
        box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "TextFillColorPrimaryBrush");
        return box;
    }

    /// <summary>A settings row: icon, title and description on the left, a control on the right.</summary>
    public static CardControl Card(SymbolRegular icon, string title, string? description, UIElement? control)
    {
        var header = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(Text(title, 14));
        if (!string.IsNullOrEmpty(description))
        {
            var desc = Text(description, 12, secondary: true);
            desc.Margin = new Thickness(0, 2, 0, 0);
            header.Children.Add(desc);
        }
        return new CardControl
        {
            Icon = Icon(icon, 20),
            Header = header,
            Content = control,
            Margin = new Thickness(0, 0, 0, 4),
        };
    }

    public static WpfTextBlock SectionTitle(string text)
    {
        var block = Text(text, 14, weight: FontWeights.SemiBold);
        block.Margin = new Thickness(2, 18, 0, 8);
        return block;
    }

    public static ToggleSwitch Toggle(bool value, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsChecked = value };
        toggle.Checked += (_, _) => changed(true);
        toggle.Unchecked += (_, _) => changed(false);
        return toggle;
    }
}
