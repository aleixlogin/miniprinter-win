using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MiniPrinter.Control;
using MiniPrinter.Gui;

namespace MiniPrinter.Tray;

public enum TrayState
{
    NoService,
    Disconnected,
    Connected,
    Printing,
    Alarm,
}

/// <summary>Notification-area icon with a context menu and balloon notifications (tasks 8.1, 8.5).</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _connectItem;
    private readonly ToolStripMenuItem _quickNoteItem;

    /// <summary>Shows the configured hotkey next to "Nota rápida…".</summary>
    public void SetQuickNoteHotkey(string? hotkey) => _quickNoteItem.ShortcutKeyDisplayString = hotkey;
    private readonly Dictionary<TrayState, Icon> _icons = [];
    private TrayState _state = (TrayState)(-1);

    private readonly ToolStripMenuItem _keepAliveItem;

    /// <summary>Reflects the keep-alive setting in the menu check mark.</summary>
    public void SetKeepAlive(bool enabled) => _keepAliveItem.Checked = enabled;

    /// <summary>What the "Plantillas" submenu offers: the favorites (which print at once) and the recent templates (which open the panel).</summary>
    public sealed record TemplateMenuSource(Func<IReadOnlyList<TrayTemplateItem>> Favorites, Func<IReadOnlyList<TrayTemplateItem>> Recent,
        Action<string, string> PrintFavorite, Action<string> OpenTemplate);

    public TrayIcon(Action openPanel, Action toggleConnection, Action testPrint, Action feed, Action exit,
        Action printClipboard, Action quickNote, Action toggleKeepAlive, Action checkUpdates, TemplateMenuSource templates)
    {
        _statusItem = new ToolStripMenuItem("MiniPrinter") { Enabled = false };
        _connectItem = new ToolStripMenuItem(Strings.Get("Tray.Connect"), null, (_, _) => toggleConnection());
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(Strings.Get("Tray.OpenPanel"), null, (_, _) => openPanel()) { Font = new Font(menu.Font, System.Drawing.FontStyle.Bold) });
        menu.Items.Add(_connectItem);
        _keepAliveItem = new ToolStripMenuItem(Strings.Get("Tray.KeepAlive"), null, (_, _) => toggleKeepAlive());
        menu.Items.Add(_keepAliveItem);
        menu.Items.Add(new ToolStripMenuItem(Strings.Get("Tray.TestPrint"), null, (_, _) => testPrint()));
        menu.Items.Add(new ToolStripMenuItem(Strings.Get("Tray.FeedPaper"), null, (_, _) => feed()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(Strings.Get("Tray.PrintClipboard"), null, (_, _) => printClipboard()));
        _quickNoteItem = new ToolStripMenuItem(Strings.Get("Tray.QuickNote"), null, (_, _) => quickNote());
        menu.Items.Add(_quickNoteItem);
        menu.Items.Add(TemplatesMenu(templates));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(Strings.Get("Tray.CheckUpdates"), null, (_, _) => checkUpdates()));
        menu.Items.Add(new ToolStripMenuItem(Strings.Get("Tray.Exit"), null, (_, _) => exit()));

        _icon = new NotifyIcon { ContextMenuStrip = menu, Visible = true, Text = "MiniPrinter" };
        _icon.BalloonTipClicked += (_, _) => { var action = _balloonAction; _balloonAction = null; action?.Invoke(); };
        _icon.BalloonTipClosed += (_, _) => _balloonAction = null;
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) openPanel(); };
        Update(TrayState.NoService, Strings.Get("Tray.ConnectingService"), connected: false);
    }

    /// <summary>The "Plantillas" submenu: built again each time it opens, so it always shows the favorites and recent ones of now.</summary>
    private ToolStripMenuItem TemplatesMenu(TemplateMenuSource source)
    {
        var root = new ToolStripMenuItem(Strings.Get("Tray.Templates"));
        var favorites = new ToolStripMenuItem(Strings.Get("Tray.Favorites"));
        var recent = new ToolStripMenuItem(Strings.Get("Tray.Recent"));
        root.DropDownItems.Add(favorites);
        root.DropDownItems.Add(recent);
        favorites.DropDownOpening += (_, _) => Fill(favorites, source.Favorites(), source);
        recent.DropDownOpening += (_, _) => Fill(recent, source.Recent(), source);
        // The submenus are empty until they open; they need an item to show an arrow.
        favorites.DropDownItems.Add(new ToolStripMenuItem("…") { Enabled = false });
        recent.DropDownItems.Add(new ToolStripMenuItem("…") { Enabled = false });
        return root;
    }

    private void Fill(ToolStripMenuItem parent, IReadOnlyList<TrayTemplateItem> items, TemplateMenuSource source)
    {
        parent.DropDownItems.Clear();
        foreach (var item in items)
            parent.DropDownItems.Add(ToMenuItem(item, source));
        // Items made after the theme was applied take the colours of the menu.
        StyleItems(parent);
    }

    private ToolStripItem ToMenuItem(TrayTemplateItem item, TemplateMenuSource source)
    {
        var menuItem = new ToolStripMenuItem(item.Text.Replace("&", "&&"));
        switch (item.Kind)
        {
            case TrayTemplateKind.Favorite:
                menuItem.Click += (_, _) => source.PrintFavorite(item.Template!, item.Favorite!);
                break;
            case TrayTemplateKind.Recent:
                menuItem.Click += (_, _) => source.OpenTemplate(item.Template!);
                break;
            case TrayTemplateKind.Group:
                foreach (var child in item.Children ?? [])
                    menuItem.DropDownItems.Add(ToMenuItem(child, source));
                StyleItems(menuItem);
                break;
            default:
                menuItem.Enabled = false;
                break;
        }
        return menuItem;
    }

    private void StyleItems(ToolStripMenuItem parent)
    {
        var menu = _icon.ContextMenuStrip!;
        parent.DropDown.BackColor = menu.BackColor;
        foreach (ToolStripItem item in parent.DropDownItems)
        {
            item.ForeColor = menu.ForeColor;
            if (item is ToolStripMenuItem sub)
                StyleItems(sub);
        }
    }

    public void Update(TrayState state, string tooltip, bool connected)
    {
        if (state != _state)
        {
            _state = state;
            _icon.Icon = IconFor(state);
        }
        // NotifyIcon.Text is limited to 127 characters.
        var text = $"MiniPrinter — {tooltip}";
        _icon.Text = text.Length > 127 ? text[..127] : text;
        _statusItem.Text = tooltip;
        _connectItem.Text = connected ? Strings.Get("Tray.Disconnect") : Strings.Get("Tray.Connect");
    }

    private Action? _balloonAction;

    /// <summary>Shows a notification; <paramref name="onClick"/> runs if the user clicks it.</summary>
    public void Notify(string title, string message, bool warning, Action? onClick)
    {
        _balloonAction = onClick;
        Notify(title, message, warning);
    }

    public void Notify(string title, string message, bool warning)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.BalloonTipIcon = warning ? ToolTipIcon.Warning : ToolTipIcon.Info;
        _icon.ShowBalloonTip(5000);
    }

    public static TrayState StateOf(StatusDto? status, bool serviceAvailable)
    {
        if (!serviceAvailable || status is null)
            return TrayState.NoService;
        if (status.Alarms.Count > 0 || status.Link == "Error")
            return TrayState.Alarm;
        if (status.Printing)
            return TrayState.Printing;
        return status.Link == "Connected" ? TrayState.Connected : TrayState.Disconnected;
    }

    /// <summary>Paints the menu with the palette in use (light, dark or high contrast).</summary>
    public void ApplyTheme(MiniPrinter.Gui.EffectiveTheme theme)
    {
        TrayMenuTheme.Apply(_icon.ContextMenuStrip!, theme);
        foreach (ToolStripItem item in _icon.ContextMenuStrip!.Items)
            if (item is ToolStripMenuItem menuItem)
                StyleItems(menuItem);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        foreach (var icon in _icons.Values)
            icon.Dispose();
    }

    private Icon IconFor(TrayState state)
    {
        if (_icons.TryGetValue(state, out var cached))
            return cached;

        var color = state switch
        {
            TrayState.Connected => Color.FromArgb(46, 160, 67),   // theme-ok: colour of the tray icon state
            TrayState.Printing => Color.FromArgb(9, 105, 218),   // theme-ok: colour of the tray icon state
            TrayState.Alarm => Color.FromArgb(207, 34, 46),   // theme-ok: colour of the tray icon state
            TrayState.Disconnected => Color.FromArgb(110, 119, 129),   // theme-ok: colour of the tray icon state
            _ => Color.FromArgb(154, 103, 0),   // theme-ok: colour of the tray icon state
        };
        // Base: the application icon at the notification area's size for the current scale
        // (16 px at 100 %, 24 px at 150 %…), with the status dot in the bottom-right corner.
        var size = SystemInformation.SmallIconSize.Width;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/miniprinter.ico"));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                using var appIcon = new Icon(stream, size, size);
                g.DrawIcon(appIcon, new Rectangle(0, 0, size, size));
            }
            var dot = Math.Max(6, size * 7 / 16);
            var x = size - dot;
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, x, x, dot - 1, dot - 1);
            using var ring = new Pen(Color.White, Math.Max(1f, size / 16f));
            g.DrawEllipse(ring, x, x, dot - 1, dot - 1);
        }
        var icon = Icon.FromHandle(bitmap.GetHicon());
        _icons[state] = icon;
        return icon;
    }
}
