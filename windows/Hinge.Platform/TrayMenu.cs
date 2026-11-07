using System.Drawing;
using System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace Hinge.Platform;

// Keep the native menu's keyboard navigation, dismissal and work-area positioning.
internal sealed class TrayMenu : Forms.ContextMenuStrip
{
    private readonly Font _menuFont = new("Segoe UI", 9f);

    internal TrayMenu()
    {
        Font = _menuFont;
        ShowImageMargin = false;
        ShowCheckMargin = false;
        Renderer = new MenuRenderer();
        DropShadowEnabled = true;
        BackColor = Color.White;
        ForeColor = Color.FromArgb(32, 33, 35);
    }

    // The native menu recalculates Padding and its preferred width on every layout.
    protected override Forms.Padding DefaultPadding => new(Scaled(8));

    private int Scaled(int value) => (int)Math.Round(value * DeviceDpi / 96f);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var size = base.GetPreferredSize(proposedSize);
        if (MinimumSize.Width > 0) size.Width = MinimumSize.Width;
        return size;
    }

    internal void PrepareForDisplay()
    {
        BackColor = Forms.SystemInformation.HighContrast ? SystemColors.Menu : Color.White;
        ForeColor = Forms.SystemInformation.HighContrast ? SystemColors.MenuText : Color.FromArgb(32, 33, 35);
        SuspendLayout();
        Padding = DefaultPadding;
        var width = Scaled(24);
        foreach (Forms.ToolStripItem item in Items)
        {
            width = Math.Max(width, Forms.TextRenderer.MeasureText(item.Text, Font, Size.Empty,
                Forms.TextFormatFlags.SingleLine | Forms.TextFormatFlags.NoPadding |
                Forms.TextFormatFlags.HidePrefix).Width + Scaled(24));
        }
        foreach (Forms.ToolStripItem item in Items)
        {
            item.AutoSize = false;
            item.Size = new Size(width, Math.Max(Scaled(32), Font.Height + Scaled(12)));
            // ToolStripMenuItem subtracts the menu left padding from its assigned bounds.
            item.Margin = new Forms.Padding(Padding.Left, 0, 0, 0);
        }
        MinimumSize = new Size(width + Padding.Horizontal, 0);
        ResumeLayout(true);
    }

    protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
    {
        PrepareForDisplay();
        base.OnOpening(e);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width < 2 || Height < 2) return;
        using var path = Rounded(new Rectangle(0, 0, Width, Height), 8 * DeviceDpi / 96f);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    internal static GraphicsPath Rounded(Rectangle bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _menuFont.Dispose();
    }

    private sealed class MenuRenderer : Forms.ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(e.ToolStrip.BackColor);
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || e.ToolStrip is not TrayMenu menu) return;
            var color = Forms.SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(243, 244, 245);
            using var brush = new SolidBrush(color);
            var bounds = new Rectangle(Point.Empty, e.Item.Size);
            bounds.Inflate(0, -menu.Scaled(2));
            using var path = Rounded(bounds, 5 * menu.DeviceDpi / 96f);
            var previous = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
            e.Graphics.SmoothingMode = previous;
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Forms.SystemInformation.HighContrast && e.Item.Selected
                ? SystemColors.HighlightText : e.ToolStrip?.ForeColor ?? SystemColors.MenuText;
            var inset = e.ToolStrip is TrayMenu menu ? menu.Scaled(12) : 12;
            var bounds = new Rectangle(inset, 0, Math.Max(0, e.Item.Width - inset * 2), e.Item.Height);
            Forms.TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, bounds, e.TextColor,
                Forms.TextFormatFlags.Left | Forms.TextFormatFlags.VerticalCenter |
                Forms.TextFormatFlags.SingleLine | Forms.TextFormatFlags.NoPadding |
                Forms.TextFormatFlags.HidePrefix);
        }
    }
}
