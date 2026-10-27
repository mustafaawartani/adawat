using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Adawat.Core;
using Adawat.Interop;
using Wpf.Ui.Controls;

namespace Adawat.UI;

/// <summary>
/// A short, clearly visible note at the bottom of the screen ("Saved to Downloads"), like a phone
/// toast. It never takes focus; clicking it runs its action (e.g. show the file) and closes it.
/// </summary>
public sealed class Toast : Window
{
    private static Toast? _current;

    private readonly SymbolIcon _icon;
    private readonly System.Windows.Controls.TextBlock _text;
    private readonly System.Windows.Controls.TextBlock _detail;
    private readonly DispatcherTimer _timer = new();
    private Action? _onClick;
    private IntPtr _hwnd;

    private Toast()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Left = -10000;
        Top = -10000;
        FontFamily = new FontFamily(Ui.FontFamilyName);
        Cursor = Cursors.Hand;
        ThemeHelper.ApplyPalette(Resources);

        _icon = new SymbolIcon { FontSize = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        _text = new System.Windows.Controls.TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _detail = new System.Windows.Controls.TextBlock { FontSize = 12, Opacity = 0.75, Margin = new Thickness(0, 2, 0, 0), MaxWidth = 420, TextTrimming = TextTrimming.CharacterEllipsis };
        // Dark on a light theme and light on a dark one, so it stands out from any window behind it.
        foreach (var block in new[] { _text, _detail }) block.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Adawat.Surface");
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_text);
        texts.Children.Add(_detail);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_icon);
        row.Children.Add(texts);
        var pill = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 11, 20, 12),
            Margin = new Thickness(18, 12, 18, 24),
            Child = row,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Direction = 270, Opacity = 0.25 },
        };
        pill.SetResourceReference(Border.BackgroundProperty, "Adawat.Text");
        Content = pill;

        _timer.Tick += (_, _) => FadeOut();
        MouseLeftButtonUp += (_, _) =>
        {
            var action = _onClick;
            FadeOut();
            action?.Invoke();
        };
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => _timer.Start();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW));
        };
    }

    /// <summary>Shows a toast at the bottom of the screen the pointer is on.</summary>
    public static void Show(string text, string? detail = null, bool success = true, Action? onClick = null, double seconds = 2.8)
    {
        try
        {
            _current ??= new Toast();
            _current.Present(text, detail, success, onClick, seconds);
        }
        catch (Exception ex)
        {
            Log.Error("Could not show a notification", ex);
        }
    }

    private void Present(string text, string? detail, bool success, Action? onClick, double seconds)
    {
        ThemeHelper.ApplyPalette(Resources);
        _text.Text = text;
        _detail.Text = detail ?? "";
        _detail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
        _icon.Symbol = success ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.ErrorCircle24;
        _icon.Foreground = new SolidColorBrush(success ? ThemeHelper.IsDark ? Color.FromRgb(0x15, 0x80, 0x3D) : Color.FromRgb(0x4A, 0xDE, 0x80)
                                                       : Color.FromRgb(0xF8, 0x71, 0x71));
        _onClick = onClick;
        Cursor = onClick != null ? Cursors.Hand : Cursors.Arrow;

        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }
        UpdateLayout();
        var cursor = WindowInfo.CursorPosition();
        var work = WindowInfo.WorkAreaAt(cursor);
        if (Native.GetWindowRect(_hwnd, out var r))
        {
            int x = work.Left + (work.Width - r.Width) / 2;
            int y = work.Bottom - r.Height;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(seconds);
        _timer.Start();
    }

    private void FadeOut()
    {
        _timer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (Opacity == 0) Hide();
        };
        BeginAnimation(OpacityProperty, fade);
    }
}
