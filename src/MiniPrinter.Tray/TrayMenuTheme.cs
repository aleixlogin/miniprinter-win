using System.Drawing;
using System.Windows.Forms;
using MiniPrinter.Gui;

namespace MiniPrinter.Tray;

/// <summary>
/// Paints the context menu of the tray icon (a Windows Forms control, outside WPF) with the palette in use. Light and
/// high contrast keep the renderer of the system, which already follows those settings; dark uses the colours below,
/// the same as the dark palette of the panel.
/// </summary>
internal static class TrayMenuTheme
{
    private static readonly Color DarkSurface = Color.FromArgb(0x16, 0x1B, 0x22);   // theme-ok: the dark palette for a Windows Forms menu
    private static readonly Color DarkText = Color.FromArgb(0xE6, 0xED, 0xF3);   // theme-ok: the dark palette for a Windows Forms menu
    private static readonly Color DarkBorder = Color.FromArgb(0x30, 0x36, 0x3D);   // theme-ok: the dark palette for a Windows Forms menu
    private static readonly Color DarkHover = Color.FromArgb(0x21, 0x26, 0x2D);   // theme-ok: the dark palette for a Windows Forms menu
    private static readonly Color DarkSelection = Color.FromArgb(0x1F, 0x3A, 0x5F);   // theme-ok: the dark palette for a Windows Forms menu

    public static void Apply(ContextMenuStrip menu, EffectiveTheme theme)
    {
        if (theme == EffectiveTheme.Dark)
        {
            menu.Renderer = new ToolStripProfessionalRenderer(new DarkColors()) { RoundedEdges = false };
            menu.BackColor = DarkSurface;
            menu.ForeColor = DarkText;
        }
        else
        {
            menu.Renderer = new ToolStripProfessionalRenderer();
            menu.BackColor = SystemColors.Menu;
            menu.ForeColor = SystemColors.MenuText;
        }
        foreach (ToolStripItem item in menu.Items)
            item.ForeColor = menu.ForeColor;
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        public DarkColors() => UseSystemColors = false;

        public override Color ToolStripDropDownBackground => DarkSurface;
        public override Color MenuBorder => DarkBorder;
        public override Color MenuItemBorder => DarkBorder;
        public override Color MenuItemSelected => DarkHover;
        public override Color MenuItemSelectedGradientBegin => DarkHover;
        public override Color MenuItemSelectedGradientEnd => DarkHover;
        public override Color MenuItemPressedGradientBegin => DarkHover;
        public override Color MenuItemPressedGradientMiddle => DarkHover;
        public override Color MenuItemPressedGradientEnd => DarkHover;
        public override Color ImageMarginGradientBegin => DarkSurface;
        public override Color ImageMarginGradientMiddle => DarkSurface;
        public override Color ImageMarginGradientEnd => DarkSurface;
        public override Color SeparatorDark => DarkBorder;
        public override Color SeparatorLight => DarkBorder;
        public override Color CheckBackground => DarkSelection;
        public override Color CheckSelectedBackground => DarkSelection;
        public override Color CheckPressedBackground => DarkSelection;
    }
}
