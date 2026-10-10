namespace FMSC_SMS.Core;

/// <summary>Non-activating key badges shown only while Ctrl is held in this application.</summary>
internal static class KeyboardShortcutHints
{
    private static readonly HashSet<ApplicationWindowForm> Windows = new();
    private static readonly List<Form> Badges = new();
    private static readonly System.Windows.Forms.Timer Timer = new() { Interval = 150 };
    private static Control? shownRoot;
    private static string shownSignature = string.Empty;

    static KeyboardShortcutHints() => Timer.Tick += (_, _) => Refresh();

    internal static void Register(ApplicationWindowForm window)
    {
        Windows.Add(window);
        Timer.Start();
    }

    internal static void Unregister(ApplicationWindowForm window)
    {
        Windows.Remove(window);
        Hide();
        if (Windows.Count == 0) Timer.Stop();
    }

    private static void Refresh()
    {
        if (Form.ActiveForm is ApplicationWindowForm active && !active.IsDisposed && active.ContainsFocus)
            PageActionShortcutController.ActiveContent(active).Controller.RefreshHints();
        if ((Control.ModifierKeys & Keys.Control)==0 || Form.ActiveForm is not ApplicationWindowForm window ||
            window.IsDisposed || !window.Enabled || !window.ContainsFocus || !Windows.Contains(window))
        {
            Hide();
            return;
        }
        var (root, controller) = PageActionShortcutController.ActiveContent(window);
        var available=controller.AvailableHints().AsEnumerable();
        if(!ReferenceEquals(controller,window.ShortcutController))available=available.Concat(window.ShortcutController.LocalHints());
        var hints = available.GroupBy(hint => hint.Target)
            .Select(group => (Target: group.Key, Key: string.Join(" / ", group.Select(hint => hint.Key)))).ToList();
        string signature = string.Join("|", hints.Select(hint =>
            $"{hint.Target.GetHashCode()}:{hint.Target.RectangleToScreen(hint.Target.ClientRectangle)}:{hint.Key}"));
        if (ReferenceEquals(root, shownRoot) && signature == shownSignature) return;
        Hide();
        shownRoot = root;
        shownSignature = signature;
        foreach (var hint in hints)
        {
            Rectangle target = hint.Target.RectangleToScreen(hint.Target.ClientRectangle);
            Rectangle viewport = window.RectangleToScreen(window.ClientRectangle);
            if (!viewport.IntersectsWith(target)) continue;
            var badge = new KeyBadge(hint.Key);
            Rectangle bounds = Screen.FromControl(hint.Target).WorkingArea;
            badge.Location = new Point(
                Math.Clamp(target.Right - badge.Width, bounds.Left, Math.Max(bounds.Left, bounds.Right - badge.Width)),
                Math.Clamp(target.Bottom - 5, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - badge.Height)));
            Badges.Add(badge);
            badge.Show(window);
        }
    }

    private static void Hide()
    {
        foreach (Form badge in Badges) badge.Dispose();
        Badges.Clear();
        shownRoot = null;
        shownSignature = string.Empty;
    }

    private sealed class KeyBadge : Form
    {
        internal KeyBadge(string text)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = AppTheme.Text;
            var label = new Label {
                Text = text, Font = AppTheme.SmallFont, ForeColor = AppTheme.Surface,
                BackColor = AppTheme.Text, AutoSize = true, Location = new Point(6, 3)
            };
            Controls.Add(label);
            ClientSize = new Size(label.PreferredSize.Width + 12, label.PreferredSize.Height + 6);
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var parameters = base.CreateParams; parameters.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020; return parameters; }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x84) { message.Result = new IntPtr(-1); return; } // Click through to the underlying control.
            base.WndProc(ref message);
        }
    }
}
