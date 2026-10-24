namespace FMSC_SMS.Core
{
    public sealed class ThemedMenuRenderer :
        ToolStripProfessionalRenderer
    {
        public ThemedMenuRenderer()
            : base(new ThemedMenuColorTable())
        {
        }
        private Color MenuColor =>
            ControlPaint.Dark(AppTheme.Primary, 0.35f);
        private Color HoverColor =>
            AppTheme.SelectionBackground;
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush =
                new SolidBrush(MenuColor);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var rect =
                new Rectangle(Point.Empty, e.Item.Size);
            var color =
                e.Item.Selected
                    ? HoverColor
                    : MenuColor;
            using var brush =
                new SolidBrush(color);
            e.Graphics.FillRectangle(brush, rect);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen =
                new Pen(
                    ControlPaint.Dark(AppTheme.Primary, 0.50f));
            int y =
                e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor =
                e.Item.Selected ? AppTheme.SelectionText : AppTheme.MenuText;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor =
                e.Item?.Selected == true ? AppTheme.SelectionText : AppTheme.MenuText;
            base.OnRenderArrow(e);
        }
    }
    internal sealed class ThemedMenuColorTable :
        ProfessionalColorTable
    {
        private Color MenuColor =>
            ControlPaint.Dark(AppTheme.Primary, 0.35f);
        private Color HoverColor =>
            AppTheme.SelectionBackground;
        private Color BorderColor =>
            ControlPaint.Dark(AppTheme.Primary, 0.50f);
        public override Color MenuStripGradientBegin =>
            MenuColor;
        public override Color MenuStripGradientEnd =>
            MenuColor;
        public override Color ToolStripDropDownBackground =>
            MenuColor;
        public override Color ImageMarginGradientBegin =>
            MenuColor;
        public override Color ImageMarginGradientMiddle =>
            MenuColor;
        public override Color ImageMarginGradientEnd =>
            MenuColor;
        public override Color MenuItemSelected =>
            HoverColor;
        public override Color MenuItemSelectedGradientBegin =>
            HoverColor;
        public override Color MenuItemSelectedGradientEnd =>
            HoverColor;
        public override Color MenuItemPressedGradientBegin =>
            HoverColor;
        public override Color MenuItemPressedGradientMiddle =>
            HoverColor;
        public override Color MenuItemPressedGradientEnd =>
            HoverColor;
        public override Color MenuBorder =>
            BorderColor;
        public override Color MenuItemBorder =>
            BorderColor;
        public override Color SeparatorDark =>
            BorderColor;
        public override Color SeparatorLight =>
            MenuColor;
        public override Color ToolStripGradientBegin =>
            MenuColor;
        public override Color ToolStripGradientMiddle =>
            MenuColor;
        public override Color ToolStripGradientEnd =>
            MenuColor;
    }
}
