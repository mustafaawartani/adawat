using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Adawat.UI;
using Wpf.Ui.Controls;

namespace Adawat.Translator;

/// <summary>
/// A language name with a small chevron. Clicking it opens a list with a search box on top,
/// like Google Translate's language menu: type a few letters and press Enter.
/// </summary>
public sealed class LanguagePicker : Grid
{
    private readonly Border _button;
    private readonly System.Windows.Controls.TextBlock _label;
    private readonly Popup _popup;
    private readonly System.Windows.Controls.TextBox _search;
    private readonly System.Windows.Controls.TextBlock _searchHint;
    private readonly ListBox _list = new();
    private List<(string Code, string Name)> _languages = new();

    public string Code { get; private set; } = Languages.Auto;

    /// <summary>Raised when the user picks a language (not when <see cref="Select"/> is called).</summary>
    public event Action? Changed;

    public LanguagePicker()
    {
        _label = PopupUi.Text("", 13, weight: FontWeights.SemiBold);
        _label.TextWrapping = TextWrapping.NoWrap;
        _label.TextTrimming = TextTrimming.CharacterEllipsis;
        _label.MaxWidth = 150;
        _label.VerticalAlignment = VerticalAlignment.Center;
        var chevron = PopupUi.Icon(SymbolRegular.ChevronDown20, 12, "Muted");
        chevron.Margin = new Thickness(6, 1, 0, 0);
        chevron.VerticalAlignment = VerticalAlignment.Center;
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(_label);
        content.Children.Add(chevron);
        _button = new Border
        {
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(12, 5, 10, 6),
            Background = Brushes.Transparent,
            Child = content,
            Cursor = Cursors.Hand,
        };
        PopupUi.MakeClickable(_button, Open, hoverBrush: "Hover", restBrush: () => _emphasized ? "Surface" : null);
        Children.Add(_button);

        // ---- The drop-down ---------------------------------------------------------------
        _search = PopupUi.PlainTextBox(13, readOnly: false);
        _search.TextWrapping = TextWrapping.NoWrap;
        _search.AcceptsReturn = false;
        _search.VerticalAlignment = VerticalAlignment.Center;
        _searchHint = PopupUi.Text("Search languages", 13, "Faint");
        _searchHint.IsHitTestVisible = false;
        _searchHint.VerticalAlignment = VerticalAlignment.Center;
        var searchIcon = PopupUi.Icon(SymbolRegular.Search20, 14, "Faint");
        searchIcon.Margin = new Thickness(0, 0, 8, 0);
        var searchField = new Grid();
        searchField.Children.Add(_searchHint);
        searchField.Children.Add(_search);
        var searchRow = new DockPanel { Margin = new Thickness(12, 10, 12, 10) };
        DockPanel.SetDock(searchIcon, Dock.Left);
        searchRow.Children.Add(searchIcon);
        searchRow.Children.Add(searchField);
        var divider = new Border { Height = 1 };
        divider.SetResourceReference(Border.BackgroundProperty, "Adawat.Border");

        _list.MaxHeight = 320;
        _list.BorderThickness = new Thickness(0);
        _list.Background = Brushes.Transparent;
        _list.Padding = new Thickness(4);
        _list.FocusVisualStyle = null;
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetIsVirtualizing(_list, true);

        var panel = new StackPanel();
        panel.Children.Add(searchRow);
        panel.Children.Add(divider);
        panel.Children.Add(_list);
        var frame = new Border
        {
            Width = 250,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Child = panel,
            Margin = new Thickness(12, 4, 12, 16), // room for the shadow
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.16 },
        };
        frame.SetResourceReference(Border.BackgroundProperty, "Adawat.Surface");
        frame.SetResourceReference(Border.BorderBrushProperty, "Adawat.Border");

        _popup = new Popup
        {
            Child = frame,
            PlacementTarget = _button,
            Placement = PlacementMode.Bottom,
            HorizontalOffset = -12,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
        };
        Children.Add(_popup); // in the tree, so the palette brushes reach the drop-down

        _search.TextChanged += (_, _) =>
        {
            _searchHint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Fill(_search.Text);
        };
        _search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (_list.Items.Count > 0) Pick((ListBoxItem)(_list.SelectedItem ?? _list.Items[0]));
            }
            else if (e.Key == Key.Down && _list.Items.Count > 0)
            {
                e.Handled = true;
                _list.SelectedIndex = Math.Min(_list.Items.Count - 1, Math.Max(0, _list.SelectedIndex + 1));
                _list.ScrollIntoView(_list.SelectedItem);
            }
            else if (e.Key == Key.Up && _list.Items.Count > 0)
            {
                e.Handled = true;
                _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
                _list.ScrollIntoView(_list.SelectedItem);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _popup.IsOpen = false;
            }
        };
        _list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (ItemFrom(e.OriginalSource as DependencyObject) is { } item) Pick(item);
        };
    }

    private bool _emphasized;

    /// <summary>Shown as the raised, white chip in the language capsule (the language translated to).</summary>
    public bool Emphasized
    {
        get => _emphasized;
        set
        {
            _emphasized = value;
            if (value)
            {
                _button.SetResourceReference(Border.BackgroundProperty, "Adawat.Surface");
                _button.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Direction = 270, Opacity = 0.14 };
            }
            else
            {
                _button.Background = Brushes.Transparent;
                _button.Effect = null;
            }
        }
    }

    public bool IsOpen => _popup.IsOpen;

    public void Close() => _popup.IsOpen = false;

    /// <summary>Sets the languages offered (favorites first) without raising <see cref="Changed"/>.</summary>
    public void SetLanguages(IEnumerable<(string Code, string Name)> languages)
    {
        _languages = languages.ToList();
        _label.Text = NameOf(Code);
    }

    public void Select(string code)
    {
        var match = _languages.FirstOrDefault(l => Languages.Same(l.Code, code));
        Code = match.Code ?? (_languages.Count > 0 ? _languages[0].Code : code);
        _label.Text = NameOf(Code);
    }

    private string NameOf(string code) => _languages.FirstOrDefault(l => Languages.Same(l.Code, code)).Name ?? Languages.NameOf(code);

    private void Open()
    {
        _search.Text = "";
        Fill("");
        _popup.IsOpen = true;
        Dispatcher.BeginInvoke(() =>
        {
            _search.Focus();
            Keyboard.Focus(_search);
            if (_list.SelectedItem != null) _list.ScrollIntoView(_list.SelectedItem);
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void Fill(string filter)
    {
        _list.Items.Clear();
        filter = filter.Trim();
        foreach (var (code, name) in _languages)
        {
            if (filter.Length > 0
                && !name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                && !code.Equals(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var item = new ListBoxItem
            {
                Content = name,
                Tag = code,
                FontSize = 13,
                Padding = new Thickness(10, 6, 10, 6),
            };
            item.SetResourceReference(Control.ForegroundProperty, "Adawat.Text");
            _list.Items.Add(item);
            if (filter.Length == 0 && Languages.Same(code, Code)) _list.SelectedItem = item;
        }
        if (filter.Length > 0 && _list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    private void Pick(ListBoxItem item)
    {
        _popup.IsOpen = false;
        if (item.Tag is not string code) return;
        bool changed = !Languages.Same(code, Code);
        Code = code;
        _label.Text = NameOf(code);
        if (changed) Changed?.Invoke();
    }

    private static ListBoxItem? ItemFrom(DependencyObject? source)
    {
        while (source != null && source is not ListBoxItem)
            source = source is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        return source as ListBoxItem;
    }
}
