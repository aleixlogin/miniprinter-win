using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MiniPrinter.Control;

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

    public TrayIcon(Action openPanel, Action toggleConnection, Action testPrint, Action feed, Action exit,
        Action printClipboard, Action quickNote, Action toggleKeepAlive)
    {
        _statusItem = new ToolStripMenuItem("MiniPrinter") { Enabled = false };
        _connectItem = new ToolStripMenuItem("Conectar", null, (_, _) => toggleConnection());
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Abrir panel", null, (_, _) => openPanel()) { Font = new Font(menu.Font, System.Drawing.FontStyle.Bold) });
        menu.Items.Add(_connectItem);
        _keepAliveItem = new ToolStripMenuItem("Mantener activa (keep-alive)", null, (_, _) => toggleKeepAlive());
        menu.Items.Add(_keepAliveItem);
        menu.Items.Add(new ToolStripMenuItem("Imprimir página de prueba", null, (_, _) => testPrint()));
        menu.Items.Add(new ToolStripMenuItem("Avanzar papel", null, (_, _) => feed()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Imprimir portapapeles", null, (_, _) => printClipboard()));
        _quickNoteItem = new ToolStripMenuItem("Nota rápida…", null, (_, _) => quickNote());
        menu.Items.Add(_quickNoteItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Salir", null, (_, _) => exit()));

        _icon = new NotifyIcon { ContextMenuStrip = menu, Visible = true, Text = "MiniPrinter" };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) openPanel(); };
        Update(TrayState.NoService, "Conectando con el servicio…", connected: false);
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
        _connectItem.Text = connected ? "Desconectar" : "Conectar";
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
            TrayState.Connected => Color.FromArgb(46, 160, 67),
            TrayState.Printing => Color.FromArgb(9, 105, 218),
            TrayState.Alarm => Color.FromArgb(207, 34, 46),
            TrayState.Disconnected => Color.FromArgb(110, 119, 129),
            _ => Color.FromArgb(154, 103, 0),
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
