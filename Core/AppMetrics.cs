namespace FMSC_SMS.Core
{
    // UI tuning index: ../UI_TUNING_GUIDE.md links each visual adjustment to its owner.
    public static class AppMetrics
    {
        public static int PagePadding =>
            AppScale.Scale(18, AppScale.SpacingScale);
        public static int PageTopPadding =>
            AppScale.Scale(12, AppScale.SpacingScale);
        public static int SectionGap =>
            AppScale.Scale(12, AppScale.SpacingScale);
        public static int SpacingXs =>
            AppScale.Scale(4, AppScale.SpacingScale);
        public static int SpacingSm =>
            AppScale.Scale(8, AppScale.SpacingScale);
        public static int SpacingMd =>
            AppScale.Scale(12, AppScale.SpacingScale);
        public static int SpacingLg =>
            AppScale.Scale(16, AppScale.SpacingScale);
        public static int SpacingXl =>
            AppScale.Scale(20, AppScale.SpacingScale);
        public static int SpacingXxl =>
            AppScale.Scale(24, AppScale.SpacingScale);
        // Dashboard minimum size Tuned for 1366 x 768 displays while remaining comfortable on larger screens.
        public const int DashboardMinimumWidth = 1080;
        public const int DashboardMinimumHeight = 600;
        public const int DashboardPagePadding = 18;
        // General controls
        public static int ControlHeight =>
            AppTheme.InputHeight;
        public static int ButtonHeight =>
            AppTheme.ButtonHeight;
        public static int LargeButtonHeight =>
            AppTheme.LargeButtonHeight;
        public static int ActionButtonMinimumWidth =>
            AppScale.Scale(122, AppScale.ControlSizeScale);
        public const int FieldLabelGap = 26;
        public const int LabelHeight = 24;
        public const int FieldHeight = 76;
        public const int SectionTitleFontSize = 14;
        public const int SectionTitleHeight = 42;
        public const int SectionHeaderHeight = 38;
        public static int SectionPadding =>
            AppScale.Scale(20, AppScale.SpacingScale);
        // DataGridView
        public static int GridRowHeight =>
            Math.Max(38, TextHeight(AppTheme.NormalFont) + 12);
        public static int GridHeaderHeight =>
            Math.Max(42, TextHeight(AppTheme.ButtonFont) + 14);
        // Sidebar
        public const int SidebarExpandedWidth = 280;
        public const int SidebarCompactWidth = 72;
        public const int SidebarCompactBreakpoint = 1450;
        public const int SidebarBrandHeight = 76;
        public const int SidebarUserHeight = 68;
        public const int SidebarCompactUserHeight = 54;
        public const int SidebarAccountHeight = 72;
        public const int SidebarModuleHeight = 46;
        public const int SidebarChildHeight = 40;
        public const int SidebarIconSize = 24;
        public const int SidebarLogoSize = 42;
        public const int SidebarAvatarSize = 38;
        public const int SidebarToggleSize = 32;
        public const int SeparatorHeight = 1;
        // Authentication
        public const int AuthenticationTitleRowHeight = 70;
        public const int PasswordFieldRowHeight = 70;
        // Page header
        public const int PageHeaderVerticalPadding = 6;
        // Routed through AppScale so they give up some of that space on smaller screens along with everything else.
        public static int PageHeaderContentHeightAllowance =>
            AppScale.Scale(6);
        public const int PageHeaderTitleLeftPadding = 4;
        public const int PageHeaderTitleRightPadding = 8;
        public static int PageActionBarHeight =>
            AppScale.Scale(54);
        public const int PageActionStatusWidth = 260;
        public const int PageTitleHeight = 68;
        public const int PageTitleLeftPadding = 22;
        public const int PageTitleTopPadding = 15;
        // Dashboard title
        public const int DashboardTitleHeight = 58;
        public const int DashboardTitleLeftPadding = 5;
        public const int DashboardTitleTopPadding = 4;
        // Dashboard welcome panel
        public const int WelcomePanelHeight = 88;
        public const int WelcomePadding = 16;
        public const int WelcomeFontSize = 18;
        public const int NormalTextFontSize = 12;
        public const int WelcomeTextLeft = 16;
        public const int WelcomeTitleTop = 14;
        public const int WelcomeMessageTop = 46;
        // Dashboard cards
        public const int CardWidth = 210;
        public const int CardHeight = 100;
        public const int DashboardCardsAreaHeight = 145;
        public const int DashboardCardsTopPadding = 12;
        public static int CardGap =>
            AppScale.Scale(12, AppScale.SpacingScale);
        // Dashboard quick actions
        public const int QuickActionsAreaHeight = 400;
        public const int QuickActionsTopPadding = 10;
        public const int QuickActionsTitleFontSize = 13;
        public const int QuickActionsTitleTop = 4;
        // Dashboard action groups
        public const int ActionGroupWidth = 460;
        public const int ActionGroupHeight = 112;
        public const int ActionGroupLeftColumnX = 15;
        public const int ActionGroupRightColumnX = 490;
        public const int ActionGroupFirstRowY = 32;
        public const int ActionGroupSecondRowY = 156;
        public const int ActionGroupThirdRowY = 280;
        public const int ActionGroupPadding = 12;
        public const int ActionGroupTitleFontSize = 12;
        public const int ActionGroupTitleLeft = 12;
        public const int ActionGroupTitleTop = 8;
        // Dashboard action buttons
        public const int ActionButtonWidth = 215;
        public const int ActionButtonHeight = 42;
        public const int ActionButtonStartX = 12;
        public const int ActionButtonSpacing = 222;
        public const int ActionButtonTop = 36;
        public const int ActionGroupFourthRowY = 404;
        public const int ActionGroupFifthRowY = 528;
        public const int ActionGroupButtonAreaPadding = 4;
        public const int ActionButtonVerticalGap = 6;
        // Maximum dashboard quick-action content area. Reduced from 800 so a 1366 x 768 display does not force excessive vertical space.
        public const int DashboardQuickActionsAreaHeight = 620;
        // Text measurement cache
        private static readonly Dictionary<Font, int> _textHeightCache = new();
        private const int TextHeightCacheLimit = 256;
        private const int TextWidthCacheLimit = 2048;
        private static readonly Dictionary<(string Text, Font Font), int> _textWidthCache = new();
        static AppMetrics()
        {
            // Also invalidate outside the dashboard (for example, startup dialogs).
            AppScale.ScaleChanged += (_, _) => InvalidateScaleCache();
            Application.ApplicationExit += (_, _) => InvalidateScaleCache();
        }
        public static int TextHeight(Font font)
        {
            if (_textHeightCache.TryGetValue(font, out int cached))
                return cached;
            using var probe = new Label {
                AutoSize = true,
                Text = "Xg",
                Font = font
            };
            int height = Math.Max(12, probe.PreferredSize.Height);
            // Font compares by value (family, size, style, unit), so this safely reuses one cached measurement across every caller that shares one of the app's handful of theme fonts…
            if (_textHeightCache.Count >= TextHeightCacheLimit)
                _textHeightCache.Clear();
            _textHeightCache[font] = height;
            return height;
        }
        public static int TextWidth(string text, Font font, int horizontalPadding = 16)
        {
            string measuredText = string.IsNullOrEmpty(text) ? " " : text;
            var key = (measuredText, font);
            // Keep long, potentially user-entered content out of this layout cache.
            bool cacheable = measuredText.Length <= 256;
            if (cacheable && _textWidthCache.TryGetValue(key, out int cached))
                return cached + horizontalPadding;
            int width = TextRenderer.MeasureText(
                measuredText,
                font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding).Width;
            if (cacheable)
            {
                if (_textWidthCache.Count >= TextWidthCacheLimit)
                    _textWidthCache.Clear();
                _textWidthCache[key] = width;
            }
            return width + horizontalPadding;
        }
        public static void FitButtonWidth(Button button, int minWidth, int horizontalPadding = 32)
        {
            int needed =
                TextWidth(button.Text, button.Font, horizontalPadding);
            button.Width = Math.Max(minWidth, needed);
        }
        // Input control sizing
        private static int? _inputControlHeight;
        public static int InputControlHeight
        {
            get
            {
                if (_inputControlHeight is null)
                {
                    using var probe = new ComboBox {
                        Font = AppTheme.NormalFont
                    };
                    _inputControlHeight = Math.Max(ControlHeight, probe.PreferredHeight);
                }
                return _inputControlHeight.Value;
            }
        }
        public static void InvalidateScaleCache()
        {
            _inputControlHeight = null;
            _fieldRowHeight = null;
            _textHeightCache.Clear();
            _textWidthCache.Clear();
            UiTextWidthCache.Clear();
        }
        public static int MultilineInputHeight(int baselineHeight) =>
            AppScale.Scale(baselineHeight, AppScale.ControlSizeScale);
        // Field sizing
        private static int? _fieldRowHeight;
        public static int FieldRowHeightFor(int controlHeight)
        {
            int captionHeight =
                Math.Max(16, TextHeight(AppTheme.ButtonFont));
            return captionHeight + controlHeight + 8;
        }
        public static int FieldRowHeight
        {
            get
            {
                if (_fieldRowHeight is null)
                    _fieldRowHeight =
                        FieldRowHeightFor(InputControlHeight);
                return _fieldRowHeight.Value;
            }
        }
        // Card sizing
        public static int CardHeaderHeight(bool hasSubtitle = true)
        {
            int titleLineHeight =
                Math.Max(16, TextHeight(AppTheme.ButtonFont));
            int subtitleLineHeight =
                hasSubtitle
                    ? Math.Max(14, TextHeight(AppTheme.NormalFont))
                    : 0;
            return titleLineHeight + subtitleLineHeight + 5;
        }
        public static int CardContentVerticalPadding =>
            2 * SpacingMd + SpacingSm;
        public static int FieldCardHeight(
            int fieldRows,
            bool hasSubtitle = true) =>
            CardHeaderHeight(hasSubtitle) +
            fieldRows * FieldRowHeight +
            CardContentVerticalPadding;
        // Button rows
        public static int ButtonRowHeight =>
            AppTheme.CompactButtonHeight + SpacingLg + SpacingXs;
    }
}
