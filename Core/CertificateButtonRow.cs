namespace FMSC_SMS.Core;
/// <summary>Certificate buttons share responsive row layout and use uniform content-derived sizes.</summary>
public static class CertificateButtonRow
{
    public static TableLayoutPanel Create(params Control[] buttons)
    {
        int baseWidth = buttons.Length == 0 ? 0 : Math.Max(AppScale.Scale(110), buttons.Max(b => b.Width));
        TableLayoutPanel row = HumanUi.ButtonRow(buttons);
        new UniformGroup(buttons, baseWidth).Apply();
        return row;
    }
    private sealed class UniformGroup
    {
        private readonly Control[] _buttons;
        private readonly int _baseWidth;
        private bool _applying;
        public UniformGroup(Control[] buttons, int baseWidth)
        {
            _buttons = buttons;
            _baseWidth = baseWidth;
            foreach (Control button in _buttons)
            {
                button.SizeChanged += (_, _) => Apply();
                button.TextChanged += (_, _) => Apply();
                button.FontChanged += (_, _) => Apply();
            }
        }
        public void Apply()
        {
            if (_applying || _buttons.Length == 0)
                return;
            _applying = true;
            try
            {
                // Widest label in the group (padding matches V2Page.RefreshActionButtons), never narrower than the width the row was built with.
                int width = _baseWidth;
                foreach (Control button in _buttons)
                    width = Math.Max(width, AppMetrics.TextWidth(button.Text, button.Font, AppScale.Scale(32)));
                int height = Math.Max(AppTheme.CompactButtonHeight,
                    _buttons.Max(button => AppMetrics.TextHeight(button.Font)) + AppScale.Scale(8));
                var margin = new Padding(AppScale.Scale(4), 0, AppScale.Scale(4), 0);
                foreach (Control button in _buttons)
                {
                    if (button.Margin != margin)
                        button.Margin = margin;
                    if (button.Width != width || button.Height != height)
                        button.Size = new Size(width, height);
                }
            }
            finally
            {
                _applying = false;
            }
        }
    }
}
