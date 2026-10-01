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
    private readonly Dictionary<TrayState, Icon> _icons = [];
    private TrayState _state = (TrayState)(-1);

    public TrayIcon(Action openPanel, Action toggleConnection, Action testPrint, Action feed, Action exit)
    {
        _statusItem = new ToolStripMenuItem("MiniPrinter") { Enabled = false };
        _connectItem = new ToolStripMenuItem("Conectar", null, (_, _) => toggleConnection());
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Abrir panel", null, (_, _) => openPanel()) { Font = new Font(menu.Font, System.Drawing.FontStyle.Bold) });
        menu.Items.Add(_connectItem);
        menu.Items.Add(new ToolStripMenuItem("Imprimir página de prueba", null, (_, _) => testPrint()));
        menu.Items.Add(new ToolStripMenuItem("Avanzar papel", null, (_, _) => feed()));
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
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var body = new SolidBrush(Color.FromArgb(36, 41, 47));
            using var paper = new SolidBrush(Color.White);
            using var outline = new Pen(Color.FromArgb(36, 41, 47), 2);
            g.FillRectangle(paper, 9, 2, 14, 12);          // paper coming out
            g.DrawRectangle(outline, 9, 2, 14, 12);
            g.FillRectangle(body, 3, 12, 26, 14);           // printer body
            g.FillRectangle(paper, 8, 16, 16, 2);           // slot
            using var dot = new SolidBrush(color);
            g.FillEllipse(dot, 19, 19, 12, 12);             // status dot
            g.DrawEllipse(new Pen(Color.White, 1.5f), 19, 19, 12, 12);
        }
        var icon = Icon.FromHandle(bitmap.GetHicon());
        _icons[state] = icon;
        return icon;
    }
}
