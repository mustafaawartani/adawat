using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Adawat.Interop;
using Adawat.UI;
using Drawing = System.Drawing;

namespace Adawat.Ocr;

/// <summary>
/// Freezes the screen and lets the user drag a rectangle around the text to read,
/// like the Snipping Tool. Returns the selected part of the screenshot.
/// </summary>
public sealed class SnipWindow : Window
{
    private readonly Drawing.Bitmap _screen;
    private readonly Drawing.Rectangle _bounds;
    private readonly Canvas _canvas = new();
    private readonly Path _shade;
    private readonly Rectangle _frame;
    private readonly Border _hint;
    private readonly TaskCompletionSource<Drawing.Bitmap?> _result = new();
    private Point _start;
    private bool _dragging;
    private IntPtr _hwnd;

    private SnipWindow(Drawing.Bitmap screen, Drawing.Rectangle bounds)
    {
        _screen = screen;
        _bounds = bounds;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        Left = -32000;
        Top = -32000;
        Width = 100;
        Height = 100;
        Background = Brushes.Black;

        var image = new Image { Source = ToBitmapSource(screen), Stretch = Stretch.Fill };
        _shade = new Path { Fill = new SolidColorBrush(Color.FromArgb(0x78, 0, 0, 0)) };
        _frame = new Rectangle
        {
            Stroke = new SolidColorBrush(ThemeHelper.AccentColor),
            StrokeThickness = 2,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        var hintText = new TextBlock
        {
            Text = "Drag around the text you want to read  ·  Esc to cancel",
            Foreground = Brushes.White,
            FontSize = 14,
            FontFamily = new FontFamily(Ui.FontFamilyName),
        };
        _hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x20, 0x20, 0x20)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8),
            Child = hintText,
            IsHitTestVisible = false,
        };

        var grid = new Grid();
        grid.Children.Add(image);
        grid.Children.Add(_canvas);
        _canvas.Children.Add(_shade);
        _canvas.Children.Add(_frame);
        _canvas.Children.Add(_hint);
        Content = grid;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, _bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height, 0);
        };
        Loaded += (_, _) =>
        {
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, _bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height, 0);
            Activate();
            Focus();
            UpdateShade(new Rect());
            PlaceHint();
        };
        SizeChanged += (_, _) =>
        {
            UpdateShade(_dragging ? SelectionRect(Mouse.GetPosition(_canvas)) : new Rect());
            PlaceHint();
        };

        MouseLeftButtonDown += (_, e) =>
        {
            _start = e.GetPosition(_canvas);
            _dragging = true;
            CaptureMouse();
            _hint.Visibility = Visibility.Collapsed;
        };
        MouseMove += (_, e) =>
        {
            if (!_dragging) return;
            var rect = SelectionRect(e.GetPosition(_canvas));
            UpdateShade(rect);
            _frame.Visibility = Visibility.Visible;
            Canvas.SetLeft(_frame, rect.X);
            Canvas.SetTop(_frame, rect.Y);
            _frame.Width = rect.Width;
            _frame.Height = rect.Height;
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!_dragging) return;
            _dragging = false;
            ReleaseMouseCapture();
            Finish(SelectionRect(e.GetPosition(_canvas)));
        };
        MouseRightButtonUp += (_, _) => Finish(null);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Finish(null);
        };
        Deactivated += (_, _) =>
        {
            if (!_result.Task.IsCompleted) Finish(null);
        };
    }

    /// <summary>Captures all screens, lets the user pick an area, and returns it (or null if cancelled).</summary>
    public static Task<Drawing.Bitmap?> PickAsync()
    {
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        var screen = new Drawing.Bitmap(bounds.Width, bounds.Height, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Drawing.Graphics.FromImage(screen))
        {
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, Drawing.CopyPixelOperation.SourceCopy);
        }
        var window = new SnipWindow(screen, bounds);
        window.Show();
        return window._result.Task;
    }

    private Rect SelectionRect(Point current) => new(
        Math.Min(_start.X, current.X), Math.Min(_start.Y, current.Y),
        Math.Abs(current.X - _start.X), Math.Abs(current.Y - _start.Y));

    private void UpdateShade(Rect hole)
    {
        var full = new RectangleGeometry(new Rect(0, 0, Math.Max(1, _canvas.ActualWidth), Math.Max(1, _canvas.ActualHeight)));
        _shade.Data = hole.IsEmpty || hole.Width < 1
            ? full
            : new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(hole));
    }

    private void PlaceHint()
    {
        _hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // Center the hint on the primary screen, which sits at (0,0) of the virtual screen.
        var primary = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? _bounds;
        double ratio = _canvas.ActualWidth > 0 ? _canvas.ActualWidth / _bounds.Width : 1;
        double centerX = (primary.Left - _bounds.Left + primary.Width / 2.0) * ratio;
        double top = (primary.Top - _bounds.Top) * ratio + 28;
        Canvas.SetLeft(_hint, centerX - _hint.DesiredSize.Width / 2);
        Canvas.SetTop(_hint, top);
    }

    private void Finish(Rect? selection)
    {
        if (_result.Task.IsCompleted) return;
        Drawing.Bitmap? crop = null;
        if (selection is { Width: >= 4, Height: >= 4 } rect && _canvas.ActualWidth > 0)
        {
            // Canvas units -> screenshot pixels.
            double sx = _bounds.Width / _canvas.ActualWidth;
            double sy = _bounds.Height / _canvas.ActualHeight;
            var px = new Drawing.Rectangle((int)(rect.X * sx), (int)(rect.Y * sy), (int)Math.Ceiling(rect.Width * sx), (int)Math.Ceiling(rect.Height * sy));
            px.Intersect(new Drawing.Rectangle(0, 0, _screen.Width, _screen.Height));
            if (px.Width > 2 && px.Height > 2) crop = _screen.Clone(px, _screen.PixelFormat);
        }
        _result.TrySetResult(crop);
        Close();
        _screen.Dispose();
    }

    private static BitmapSource ToBitmapSource(Drawing.Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
            Drawing.Imaging.ImageLockMode.ReadOnly, Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var source = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32, null,
                data.Scan0, data.Stride * bitmap.Height, data.Stride);
            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
