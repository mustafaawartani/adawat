using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Adawat.Core;
using Adawat.Interop;
using Adawat.UI;

namespace Adawat.ImageZoom;

/// <summary>
/// The enlarged picture next to the pointer. It never takes focus and lets every click
/// through, so it never gets in the way of the app underneath.
/// </summary>
public sealed class ImagePreviewWindow : Window
{
    /// <summary>Room around the frame for its shadow, in DIPs.</summary>
    private const double ShadowRoom = 14;
    private const double FramePadding = 6;
    private const double CaptionHeight = 28;

    /// <summary>Everything around the picture, in DIPs: (width, height).</summary>
    public static (double Width, double Height) ChromeDip => (2 * (ShadowRoom + 1 + FramePadding), 2 * (ShadowRoom + 1 + FramePadding) + CaptionHeight);

    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly TextBlock _details;
    private readonly TextBlock _hint;
    private readonly DispatcherTimer _messageTimer = new() { Interval = TimeSpan.FromSeconds(2.2) };
    private string _hintText = "";
    private IntPtr _hwnd;

    public ImagePreviewWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Left = -10000;
        Top = -10000;
        UseLayoutRounding = true;
        FontFamily = new FontFamily(Ui.FontFamilyName);
        ThemeHelper.ApplyPalette(Resources);
        ThemeHelper.Changed += () => ThemeHelper.ApplyPalette(Resources);

        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        var picture = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = _image };

        _details = PopupUi.Text("", 12, "Muted");
        _details.TextWrapping = TextWrapping.NoWrap;
        _details.TextTrimming = TextTrimming.CharacterEllipsis;
        _details.VerticalAlignment = VerticalAlignment.Center;
        _hint = PopupUi.Text("", 12, "Faint");
        _hint.TextWrapping = TextWrapping.NoWrap;
        _hint.VerticalAlignment = VerticalAlignment.Center;
        _hint.Margin = new Thickness(12, 0, 0, 0);
        var caption = new DockPanel { Height = CaptionHeight, Margin = new Thickness(4, 0, 2, 0) };
        DockPanel.SetDock(_hint, Dock.Right);
        caption.Children.Add(_hint);
        caption.Children.Add(_details);

        var layout = new DockPanel();
        DockPanel.SetDock(caption, Dock.Bottom);
        layout.Children.Add(caption);
        layout.Children.Add(picture);

        var frame = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(FramePadding),
            Margin = new Thickness(ShadowRoom),
            Child = layout,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = 0.22 },
        };
        frame.SetResourceReference(Border.BackgroundProperty, "Adawat.Surface");
        frame.SetResourceReference(Border.BorderBrushProperty, "Adawat.Border");
        Content = frame;

        _messageTimer.Tick += (_, _) =>
        {
            _messageTimer.Stop();
            _hint.Text = _hintText;
            _hint.SetResourceReference(TextBlock.ForegroundProperty, "Adawat.Faint");
        };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
            ex |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
        };
    }

    public bool IsShown => IsVisible && Opacity > 0;

    /// <summary>Shows <paramref name="image"/> at <paramref name="layout"/> (physical pixels) on a screen with this DPI scale.</summary>
    public void ShowImage(LoadedImage image, PreviewLayout layout, double dpiScale, string hint)
    {
        _messageTimer.Stop();
        _image.Source = image.Picture;
        _image.Width = image.PixelWidth * layout.Scale / dpiScale;
        _image.Height = image.PixelHeight * layout.Scale / dpiScale;
        RenderOptions.SetBitmapScalingMode(_image, layout.Scale > 1.01 && image.IsScreenCapture ? BitmapScalingMode.Linear : BitmapScalingMode.HighQuality);

        var details = $"{image.PixelWidth} × {image.PixelHeight}";
        if (image.IsScreenCapture) details += "  ·  as shown on screen";
        else if (!string.IsNullOrWhiteSpace(image.FileName)) details += "  ·  " + image.FileName;
        if (layout.Scale < 0.995) details += $"  ·  {layout.Scale * 100:0}%";
        _details.Text = details;
        _hintText = hint;
        _hint.Text = hint;
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "Adawat.Faint");

        Width = layout.Window.Width / dpiScale;
        Height = layout.Window.Height / dpiScale;
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }
        var w = layout.Window;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, w.Left, w.Top, w.Width, w.Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(110)));
    }

    /// <summary>A short note in place of the shortcut hint, e.g. "Saved to Downloads".</summary>
    public void ShowMessage(string text, bool error = false)
    {
        _hint.Text = text;
        _hint.SetResourceReference(TextBlock.ForegroundProperty, error ? "Adawat.Muted" : "Adawat.Accent");
        _messageTimer.Stop();
        _messageTimer.Start();
    }

    public void HidePreview()
    {
        _messageTimer.Stop();
        if (!IsVisible) return;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Hide();
        _image.Source = null; // let a big picture's memory go
    }
}
