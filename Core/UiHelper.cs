namespace FMSC_SMS.Core
{
    public static partial class UiHelper
    {
        internal const string PageTitleTag =
            "UiHelper.PageTitle";
        internal const string SectionHeadingTag =
            "UiHelper.SectionHeading";
        internal const string FieldCaptionTag =
            "UiHelper.FieldCaption";
        private const string MenuSectionTag =
            "UiHelper.MenuSection";
        /// <summary>The caption label carries <see cref="FieldCaptionTag"/>, which is how it is found again - positional lookup would break if the panel's layout ever changed.</summary>
        public static void SetFieldCaption(Panel field, string caption, bool required = true)
        {
            foreach (Control control in field.Controls)
            {
                if (control is Label label &&
                    Equals(label.Tag, FieldCaptionTag))
                {
                    label.Text =
                        required
                            ? caption + " *"
                            : caption;
                    continue;
                }
                control.AccessibleDescription =
                    required
                        ? "Required"
                        : null;
                if (string.IsNullOrWhiteSpace(control.AccessibleName))
                    control.AccessibleName = caption;
                if (control is IAccessibleInput input)
                {
                    input.ApplyAccessibleInfo(
                        control.AccessibleName,
                        control.AccessibleDescription);
                }
            }
        }
        public static void DisposePictureImageOnDispose(PictureBox pictureBox)
        {
            pictureBox.Disposed += (_, _) => ReplacePictureImage(pictureBox, null);
        }
        public static void ReplaceRegion(Control control, Region? region)
        {
            Region? previous = control.Region;
            control.Region = region;
            previous?.Dispose();
        }
        public static void ReplacePictureImage(PictureBox pictureBox, Image? image)
        {
            if (ReferenceEquals(pictureBox.Image, image))
                return;
            Image? previous = pictureBox.Image;
            pictureBox.Image = image;
            previous?.Dispose();
        }
        public static Bitmap? LoadImageUnlocked(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                using var stream = new MemoryStream(bytes);
                using var source = Image.FromStream(stream);
                return new Bitmap(source);
            }
            catch (Exception nonFatalException21)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException21, "UiHelper: LoadImageUnlocked fallback");
                return null;
            }
        }
        public static Color MenuButtonHoverColor =>
                AppTheme.MenuHover;
        public static void StyleTextBox(TextBox textBox)
        {
            textBox.Font =
                AppTheme.NormalFont;
            textBox.ForeColor =
                AppTheme.Text;
            textBox.BackColor =
                AppTheme.Surface;
            textBox.Height =
                AppMetrics.InputControlHeight;
            textBox.BorderStyle =
                BorderStyle.FixedSingle;
            textBox.Multiline =
                false;
        }
        public static void StyleComboBox(ComboBox comboBox)
        {
            comboBox.Font =
                AppTheme.NormalFont;
            comboBox.ForeColor =
                AppTheme.Text;
            comboBox.BackColor =
                AppTheme.Surface;
            comboBox.Height =
                AppMetrics.InputControlHeight;
            comboBox.FlatStyle =
                FlatStyle.Flat;
        }
        private const int GlyphIconCacheLimit = 128;
        private const long GlyphIconCacheByteLimit = 1024 * 1024;
        private static long _glyphIconCacheBytes;
        private static readonly object GlyphIconCacheLock = new();
        private static readonly Dictionary<(string Glyph, int Argb, int Size), Bitmap> GlyphIconCache = new();
        static UiHelper()
        {
            AppTheme.ThemeChanged += (_, _) => ClearGlyphIconCache();
            AppScale.ScaleChanged += (_, _) => ClearGlyphIconCache();
            Application.ApplicationExit += (_, _) => ClearGlyphIconCache();
        }
        private static void ClearGlyphIconCache()
        {
            lock (GlyphIconCacheLock)
            {
                foreach (Bitmap icon in GlyphIconCache.Values)
                    icon.Dispose();
                GlyphIconCache.Clear();
                _glyphIconCacheBytes = 0;
            }
        }
        public static void DisposeOwnedButtonImageOnDispose(Button button)
        {
            // Only opt in for privately owned images, never shared image resources.
            Image? ownedImage = button.Image;
            button.Disposed += (_, _) => ownedImage?.Dispose();
        }
        public static Bitmap CreateGlyphIcon(string glyph, Color? color = null, int size = 18)
        {
            int px = Math.Max(12, size);
            Color resolvedColor = color ?? AppTheme.MutedText;
            // Large/custom artwork is generated normally so retained bitmap memory stays bounded.
            if (px > 128 || glyph.Length > 32)
                return RenderGlyphIcon(glyph, resolvedColor, px);
            var key = (glyph, resolvedColor.ToArgb(), px);
            lock (GlyphIconCacheLock)
            {
                if (!GlyphIconCache.TryGetValue(key, out Bitmap? cached))
                {
                    long bytes = (long)px * px * 4;
                    if (GlyphIconCache.Count >= GlyphIconCacheLimit ||
                        _glyphIconCacheBytes + bytes > GlyphIconCacheByteLimit)
                        ClearGlyphIconCache();
                    cached = RenderGlyphIcon(glyph, resolvedColor, px);
                    GlyphIconCache.Add(key, cached);
                    _glyphIconCacheBytes += bytes;
                }
                // Callers own their image and may mutate/dispose it independently.
                return (Bitmap)cached.Clone();
            }
        }
        private static Bitmap RenderGlyphIcon(string glyph, Color color, int px)
        {
            var bitmap = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using Graphics g = Graphics.FromImage(bitmap);
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            // This font is only needed while rendering; do not retain arbitrary icon sizes.
            float fontSize = (float)Math.Round(Math.Max(9f, px * 0.72f), 2, MidpointRounding.AwayFromZero);
            using var font = new Font("Segoe UI Symbol", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(glyph, font, brush, new RectangleF(0, 0, px, px), format);
            return bitmap;
        }
        public static Label CreateStatusBadge(string text, Color? accent = null)
        {
            var color = accent ?? AppTheme.Primary;
            return new Label {
                Text = text,
                AutoSize = true,
                Font = AppTheme.FieldCaptionFont,
                ForeColor = color,
                BackColor = AppTheme.Tint(color, 0.88f),
                Padding = new Padding(
                    AppMetrics.SpacingSm,
                    AppMetrics.SpacingXs,
                    AppMetrics.SpacingSm,
                    AppMetrics.SpacingXs),
                Margin = new Padding(0),
                TextAlign = ContentAlignment.MiddleCenter
            };
        }
        public static void ApplyModernStyle(Control root)
        {
            if (root == null)
                return;
            UiTheme.ApplyTree(root);
        }
        public static void RefreshApplicationTheme()
        {
            foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
            {
                if (form.IsDisposed) continue;
                try { ApplyThemeToForm(form); }
                catch (Exception ex) when (AppTheme.IsChangingTheme)
                { AppExceptionHandler.LogNonFatal(ex,"Refreshing form theme: "+form.GetType().Name); }
            }
        }
        internal static void RefreshControlsForScaleChange(Control root)
        {
            if (root == null)
                return;
            ApplyModernStyle(root);
            root.Invalidate(true);
        }
        internal static void ApplyThemeToForm(Form form)
        {
            if (form == null)
                return;
            var pnlMenu =
                FindControlRecursive(form, "pnlMenu");
            if (pnlMenu != null)
            {
                var menuBackground =
                    GetMenuBackground();
                pnlMenu.BackColor =
                    menuBackground;
                var logoPanel =
                    FindControlRecursive(pnlMenu, "pnlLogo");
                if (logoPanel != null)
                {
                    logoPanel.BackColor =
                        menuBackground;
                }
                var separator =
                    FindControlRecursive(pnlMenu, "pnlLogoSeparator");
                if (separator != null)
                {
                    separator.BackColor =
                        ControlPaint.Dark(AppTheme.Primary, 0.50f);
                }
                ApplyMenuTheme(pnlMenu, menuBackground);
            }
            foreach (var menuStrip
                in GetControlsOfType<MenuStrip>(form))
            {
                StyleMenuStrip(menuStrip);
            }
            ApplyModernStyle(form);
            form.Invalidate(true);
            form.Update();
        }
        private static Control? FindControlRecursive(Control parent, string name)
        {
            if (parent == null)
                return null;
            foreach (Control control
                in parent.Controls)
            {
                if (string.Equals(control.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return control;
                }
                var found =
                    FindControlRecursive(control, name);
                if (found != null)
                    return found;
            }
            return null;
        }
        private static IEnumerable<T>
            GetControlsOfType<T>(Control parent)
            where T : Control
        {
            foreach (Control control
                in parent.Controls)
            {
                if (control is T typed)
                    yield return typed;
                foreach (var child
                    in GetControlsOfType<T>(control))
                {
                    yield return child;
                }
            }
        }
        private static Color GetMenuBackground()
        {
            return ControlPaint.Dark(AppTheme.Primary, 0.35f);
        }
        private static void ApplyMenuTheme(Control menu, Color menuBackground)
        {
            menu.BackColor =
                menuBackground;
            foreach (Control control
                in menu.Controls)
            {
                if (control is Button button &&
                    button is not ModernButton)
                {
                    button.BackColor =
                        menuBackground;
                    button.ForeColor =
                        AppTheme.MenuText;
                }
                else if (control is Label label)
                {
                    if (label.Tag is string tag &&
                        tag == MenuSectionTag)
                    {
                        label.ForeColor =
                            AppTheme.MenuMutedText;
                        label.BackColor =
                            menuBackground;
                    }
                    else
                    {
                        label.BackColor =
                            menuBackground;
                    }
                }
                else if (control is Panel panel)
                {
                    panel.BackColor =
                        menuBackground;
                    ApplyMenuTheme(panel, menuBackground);
                }
            }
        }
        internal static void RefreshThemeSpecificControls(Control root) => UiTheme.ApplyTree(root);
        private static void StyleMenuStrip(MenuStrip menuStrip)
        {
            var menuColor =
                GetMenuBackground();
            menuStrip.SuspendLayout();
            menuStrip.BackColor =
                menuColor;
            menuStrip.ForeColor =
                AppTheme.MenuText;
            menuStrip.RenderMode =
                ToolStripRenderMode.Professional;
            menuStrip.Renderer =
                new ThemedMenuRenderer();
            foreach (ToolStripItem item
                in menuStrip.Items)
            {
                StyleMenuItem(item);
            }
            menuStrip.ResumeLayout();
            menuStrip.Invalidate(true);
            menuStrip.Update();
        }
        private static void StyleMenuItem(ToolStripItem item)
        {
            var menuColor =
                GetMenuBackground();
            item.ForeColor =
                AppTheme.MenuText;
            item.BackColor =
                menuColor;
            if (item is ToolStripMenuItem menuItem)
            {
                foreach (ToolStripItem child
                    in menuItem.DropDownItems)
                {
                    StyleMenuItem(child);
                }
            }
        }
    }
}
