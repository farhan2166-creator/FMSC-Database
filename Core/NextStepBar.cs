using NavigateAction = System.Action<System.Func<System.Windows.Forms.Control>, string, string>;
namespace FMSC_SMS.Core;
/// <summary>One step the operator can take next, with the student already picked: either open the next screen (<see cref="Go"/>) or run something in place such as printing a bill (<see cref="Do"/>).</summary>
public sealed class FlowStep
{
    private FlowStep(string text, string? permission, bool primary)
    {
        Text = text;
        Permission = permission;
        Primary = primary;
    }
    public string Text { get; }
    /// <summary>The permission the step's screen or procedure needs; a user without it is not offered the step.</summary>
    public string? Permission { get; }
    /// <summary>The recommended next step, drawn as the filled button.</summary>
    public bool Primary { get; }
    public string Module { get; private init; } = string.Empty;
    public string Title { get; private init; } = string.Empty;
    /// <summary>Builds the next screen from the admission id (0 = no student) and the shell's navigate delegate.</summary>
    public Func<int, NavigateAction?, Control>? Page { get; private init; }
    /// <summary>Runs in place with the admission id.</summary>
    public Func<int, Task>? Run { get; private init; }
    public static FlowStep Go(
        string text,
        string module,
        string title,
        string? permission,
        Func<int, NavigateAction?, Control> page,
        bool primary = false) =>
        new(text, permission, primary) { Module = module, Title = title, Page = page };
    public static FlowStep Do(
        string text,
        string? permission,
        Func<int, Task> run,
        bool primary = false) =>
        new(text, permission, primary) { Run = run };
}
/// <summary>The screens that normally follow one another in the front-desk work, so a page that ends a step can hand the same student to the next one with a single click.</summary>
public static class StudentFlow
{
    /// <summary>Opens Admissions on the promoted student, where Confirm Return appears when eligible.</summary>
    public static FlowStep ConfirmReturn(bool primary = false) =>
        FlowStep.Go(
            "Confirm Return Admission →",
            "Students",
            PageTitles.Admissions,
            "Students.Administer",
            (admissionId, navigate) => new Pages.Students.Admissions.StudentAdmissionPage(navigate, admissionId),
            primary);
    /// <summary>Collect Payment, opened on the student - the same screen as <see cref="FeeCollection"/>, worded as the action the front desk actually takes right after an admission is saved.</summary>
    public static FlowStep CollectPayment(bool primary = false) =>
        FlowStep.Go(
            "Collect Payment →",
            "Finance",
            "Fee Collection",
            "Fees.Collect",
            (admissionId, _) => new Pages.Finance.Collection.FeeCollectionPage(admissionId),
            primary);
    /// <summary>Fee Collection, opened on the student.</summary>
    public static FlowStep FeeCollection(bool primary = false) =>
        FlowStep.Go(
            "Fee Collection →",
            "Finance",
            "Fee Collection",
            "Fees.Collect",
            (admissionId, _) => new Pages.Finance.Collection.FeeCollectionPage(admissionId),
            primary);
}
/// <summary>A slim strip that appears when a step has finished: a message about what was done and a button for each sensible next step, each carrying the student that was just handled.</summary>
public sealed class NextStepBar : Panel
{
    private const int AccentWidth = 4;
    private readonly NavigateAction? _navigate;
    private readonly Action<Exception>? _onError;
    private readonly TableLayoutPanel _layout;
    private readonly Label _message;
    private readonly FlowLayoutPanel _buttons;
    private readonly Label _close;
    private readonly List<(ModernButton Button, bool Primary)> _stepButtons = new();
    private int _admissionId;
    private bool _busy;
    /// <param name="navigate">The shell's navigate delegate (the one the menu uses). Null = steps that open a screen are not offered.</param>
    /// <param name="onError">Where a step's failure is reported (normally the page's Fail).</param>
    public NextStepBar(NavigateAction? navigate, Action<Exception>? onError = null)
    {
        _navigate = navigate;
        _onError = onError;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        Dock = DockStyle.Fill;
        Visible = false;
        Margin = new Padding(0, 0, 0, AppScale.Scale(6));
        Padding = new Padding(AccentWidth + AppScale.Scale(10), AppScale.Scale(4), AppScale.Scale(6), AppScale.Scale(4));
        int barHeight = AppTheme.CompactButtonHeight + AppScale.Scale(16);
        Height = barHeight;
        MinimumSize = new Size(0, barHeight);
        MaximumSize = new Size(0, barHeight);
        _message = new Label {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = AppTheme.NormalFont,
            Margin = new Padding(0)
        };
        _buttons = new FlowLayoutPanel {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };
        _close = new Label {
            Text = "✕",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
            Font = AppTheme.NormalFont,
            Margin = new Padding(0)
        };
        _close.Click += (_, _) => Dismiss();
        _layout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, AppScale.Scale(28)));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _layout.Controls.Add(_message, 0, 0);
        _layout.Controls.Add(_buttons, 1, 0);
        _layout.Controls.Add(_close, 2, 0);
        Controls.Add(_layout);
        ApplyTheme();
        AppTheme.ThemeChanged += OnThemeChanged;
    }
    /// <summary>The student the visible steps will carry.</summary>
    public int AdmissionId => _admissionId;
    /// <summary>Shows <paramref name="message"/> and one button per step the user is allowed to take. <paramref name="admissionId"/> is handed to every step (0 = no particular student).</summary>
    public void Offer(string message, int admissionId, params FlowStep[] steps)
    {
        _admissionId = admissionId;
        _message.Text = message;
        ClearButtons();
        foreach (FlowStep step in steps)
        {
            if (step.Permission is not null && !CurrentUser.HasPermission(step.Permission))
                continue;
            if (step.Page is not null && _navigate is null)
                continue;
            var button = new ModernButton {
                Text = step.Text,
                Height = AppTheme.CompactButtonHeight,
                Margin = new Padding(AppScale.Scale(6), 0, 0, 0),
                Font = AppTheme.ButtonFont,
                TabStop = true
            };
            AppMetrics.FitButtonWidth(button, minWidth: 120);
            StyleButton(button, step.Primary);
            FlowStep captured = step;
            button.Click += (_, _) => _ = RunAsync(captured);
            _stepButtons.Add((button, step.Primary));
            _buttons.Controls.Add(button);
        }
        Visible = true;
        Invalidate();
    }
    /// <summary>Hides the strip. Called when the operator moves on to a different student.</summary>
    public void Dismiss()
    {
        Visible = false;
        _admissionId = 0;
        ClearButtons();
    }
    private async Task RunAsync(FlowStep step)
    {
        if (_busy || IsDisposed)
            return;
        try
        {
            int admissionId = _admissionId;
            if (step.Page is not null)
            {
                NavigateAction? navigate = _navigate;
                if (navigate is null)
                    return;
                Func<int, NavigateAction?, Control> page = step.Page;
                navigate(() => page(admissionId, navigate), step.Module, step.Title);
                return;
            }
            if (step.Run is not null)
            {
                _busy = true;
                SetButtonsEnabled(false);
                try
                {
                    await step.Run(admissionId);
                }
                finally
                {
                    _busy = false;
                    if (!IsDisposed)
                        SetButtonsEnabled(true);
                }
            }
        }
        catch (Exception ex)
        {
            if (_onError is not null)
                _onError(ex);
            else
                throw;
        }
    }
    private void SetButtonsEnabled(bool enabled)
    {
        foreach (var (button, _) in _stepButtons)
            button.Enabled = enabled;
    }
    private void ClearButtons()
    {
        foreach (var (button, _) in _stepButtons)
        {
            _buttons.Controls.Remove(button);
            button.Dispose();
        }
        _stepButtons.Clear();
    }
    private static void StyleButton(ModernButton button, bool primary)
    {
        if (primary)
            button.SetColors(AppTheme.Success);
        else
            button.SetOutlineColors(AppTheme.InputBorder, AppTheme.Primary, AppTheme.Surface);
    }
    private void ApplyTheme()
    {
        BackColor = AppTheme.SurfaceSecondary;
        _message.ForeColor = AppTheme.Text;
        _message.BackColor = AppTheme.SurfaceSecondary;
        _close.ForeColor = AppTheme.MutedText;
        _close.BackColor = AppTheme.SurfaceSecondary;
        _layout.BackColor = AppTheme.SurfaceSecondary;
        _buttons.BackColor = AppTheme.SurfaceSecondary;
        foreach (var (button, primary) in _stepButtons)
            StyleButton(button, primary);
    }
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing)
            return;
        ApplyTheme();
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (var accent = new SolidBrush(AppTheme.Success))
            e.Graphics.FillRectangle(accent, 0, 0, AccentWidth, Height);
        using var border = new Pen(AppTheme.Border, 1F);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            AppTheme.ThemeChanged -= OnThemeChanged;
        base.Dispose(disposing);
    }
}
