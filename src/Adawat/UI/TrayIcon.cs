using System;
using System.Drawing;
using System.Windows.Forms;
using Adawat.Core;

namespace Adawat.UI;

/// <summary>The notification-area icon and its menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly AppSettings _settings;
    private readonly ToolStripMenuItem _translatorItem;
    private readonly ToolStripMenuItem _gesturesItem;
    private readonly ToolStripMenuItem _imageZoomItem;
    private readonly ToolStripMenuItem _translateItem;
    private readonly ToolStripMenuItem _boxItem;
    private readonly ToolStripMenuItem _ocrItem;

    public TrayIcon(AppSettings settings, Action openBox, Action translateSelection, Action captureScreen,
        Action openSettings, Action<string> openSettingsPage, Action settingsChanged, Action exit)
    {
        _settings = settings;
        _translateItem = new ToolStripMenuItem("Translate selected text", null, (_, _) => translateSelection());
        _boxItem = new ToolStripMenuItem("Open translator", null, (_, _) => openBox()) { Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, FontStyle.Bold) };
        _ocrItem = new ToolStripMenuItem("Text from screen", null, (_, _) => captureScreen());
        _translatorItem = new ToolStripMenuItem("Translator on", null, (_, _) =>
        {
            _settings.Translator.Enabled = !_settings.Translator.Enabled;
            settingsChanged();
        });
        _gesturesItem = new ToolStripMenuItem("Touchpad gestures on", null, (_, _) =>
        {
            _settings.Gestures.Enabled = !_settings.Gestures.Enabled;
            settingsChanged();
        });
        _imageZoomItem = new ToolStripMenuItem("Picture zoom on", null, (_, _) =>
        {
            _settings.ImageZoom.Enabled = !_settings.ImageZoom.Enabled;
            settingsChanged();
        });

        var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, Padding = new Padding(2, 4, 2, 4) };
        menu.Items.Add(_boxItem);
        menu.Items.Add(_translateItem);
        menu.Items.Add(_ocrItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_translatorItem);
        menu.Items.Add(_gesturesItem);
        menu.Items.Add(_imageZoomItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("History", null, (_, _) => openSettingsPage("History")));
        menu.Items.Add(new ToolStripMenuItem("Settings", null, (_, _) => openSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit Adawat", null, (_, _) => exit()));
        foreach (ToolStripItem item in menu.Items) item.Padding = new Padding(4, 3, 8, 3);
        menu.Opening += (_, _) => Refresh(menu);

        _icon = new NotifyIcon
        {
            Icon = AppIcon.TrayIcon(SystemInformation.SmallIconSize.Width),
            Text = "Adawat",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) openBox();
        };
    }

    private void Refresh(ContextMenuStrip menu)
    {
        _translatorItem.Checked = _settings.Translator.Enabled;
        _gesturesItem.Checked = _settings.Gestures.Enabled;
        _imageZoomItem.Checked = _settings.ImageZoom.Enabled;
        _translateItem.ShortcutKeyDisplayString = _settings.Translator.TranslateHotkey;
        _boxItem.ShortcutKeyDisplayString = _settings.Translator.OpenBoxHotkey;
        _ocrItem.ShortcutKeyDisplayString = _settings.Ocr.Hotkey;

        bool dark = ThemeHelper.IsDark;
        var a = ThemeHelper.AccentColor;
        var colors = new MenuColors(dark, Color.FromArgb(a.R, a.G, a.B));
        menu.Renderer = new ToolStripProfessionalRenderer(colors) { RoundedEdges = true };
        menu.BackColor = colors.Back;
        menu.ForeColor = dark ? Color.White : Color.FromArgb(0x1A, 0x1A, 0x1A);
        foreach (ToolStripItem item in menu.Items) item.ForeColor = menu.ForeColor;
    }

    public void ShowBalloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    /// <summary>Menu colors in the app's palette: neutrals with a touch of the accent color.</summary>
    private sealed class MenuColors : ProfessionalColorTable
    {
        private readonly bool _dark;
        private readonly Color _accent;

        public MenuColors(bool dark, Color accent)
        {
            _dark = dark;
            _accent = accent;
        }

        private Color Mix(Color under, double amount) => Color.FromArgb(
            (int)(under.R + (_accent.R - under.R) * amount),
            (int)(under.G + (_accent.G - under.G) * amount),
            (int)(under.B + (_accent.B - under.B) * amount));

        public Color Back => Mix(_dark ? Color.FromArgb(0x2B, 0x2B, 0x2B) : Color.FromArgb(0xFA, 0xFA, 0xFB), _dark ? 0.08 : 0.04);
        private Color Hover => Mix(_dark ? Color.FromArgb(0x3D, 0x3D, 0x3D) : Color.FromArgb(0xEE, 0xEF, 0xF2), _dark ? 0.22 : 0.16);
        private Color Line => Mix(_dark ? Color.FromArgb(0x45, 0x45, 0x45) : Color.FromArgb(0xDD, 0xDF, 0xE4), 0.15);
        public override Color ToolStripDropDownBackground => Back;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color MenuBorder => Line;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Back;
        public override Color CheckBackground => Hover;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
    }
}
