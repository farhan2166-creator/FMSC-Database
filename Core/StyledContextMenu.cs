using System.Drawing.Drawing2D;

namespace FMSC_SMS.Core;

/// <summary>Shared popup styling for action, account and grid menus.</summary>
public sealed class StyledContextMenu : ContextMenuStrip
{
    public StyledContextMenu()
    {
        Renderer = new PopupRenderer();
        ShowImageMargin = false;
        ShowCheckMargin = false;
        Padding = new Padding(6);
        Opening += (_, _) => ApplyTheme();
        ItemAdded += (_, e) => StyleItem(e.Item);
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        Font = AppTheme.NormalFont;
        BackColor = AppTheme.Surface;
        ForeColor = AppTheme.Text;
        DropShadowEnabled = !AppTheme.UseSimpleRendering;
        foreach (ToolStripItem item in Items) StyleItem(item);
    }

    private static void StyleItem(ToolStripItem? item)
    {
        if (item is null) return;
        item.ForeColor = AppTheme.Text;
        item.BackColor = AppTheme.Surface;
        if (item is ToolStripSeparator) return;
        item.Padding = new Padding(10, 8, 10, 8);
        if (item is ToolStripMenuItem menuItem)
        {
            menuItem.DropDown.Renderer = new PopupRenderer();
            menuItem.DropDown.DropShadowEnabled = !AppTheme.UseSimpleRendering;
            foreach (ToolStripItem child in menuItem.DropDownItems) StyleItem(child);
        }
    }

    private sealed class PopupRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(AppTheme.Surface);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var rect = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
            if (AppTheme.UseSimpleRendering)
            {
                using var flatBrush = new SolidBrush(AppTheme.PrimaryLight);
                e.Graphics.FillRectangle(flatBrush, rect);
                return;
            }
            using var path = new GraphicsPath();
            const int radius = 8;
            path.AddArc(rect.Left, rect.Top, radius, radius, 180, 90);
            path.AddArc(rect.Right - radius, rect.Top, radius, radius, 270, 90);
            path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - radius, radius, radius, 90, 90);
            path.CloseFigure();
            using var brush = new SolidBrush(AppTheme.PrimaryLight);
            var previous = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
            e.Graphics.SmoothingMode = previous;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? AppTheme.MutedText : AppTheme.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled == true ? AppTheme.Primary : AppTheme.MutedText;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawLine(pen, 10, e.Item.Height / 2, e.Item.Width - 10, e.Item.Height / 2);
        }
    }
}
