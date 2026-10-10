using System.Runtime.CompilerServices;
namespace FMSC_SMS.Core;
/// <summary>UX - one shared "Enter moves to the next field" behaviour for data-entry forms and pages. Nothing is disabled or blocked, and Tab / Shift+Tab are unchanged.</summary>
public static class FormFlow
{
    internal static void ScheduleInitialFocus(Control host, Control scope, Func<bool> isCurrent)
    {
        if (host.IsDisposed || scope.IsDisposed) return;
        var waiting = new List<Control>();
        bool finished = false;
        bool queued = false;
        void StopWaiting()
        {
            finished = true;
            foreach (Control control in waiting)
            {
                control.EnabledChanged -= InputReady;
                control.VisibleChanged -= InputReady;
            }
            waiting.Clear();
            scope.Disposed -= Abandon;
        }
        void Abandon(object? sender, EventArgs e) => StopWaiting();
        void InputReady(object? sender, EventArgs e) => QueueFocus();
        void Watch(Control control)
        {
            waiting.Add(control);
            control.EnabledChanged += InputReady;
            control.VisibleChanged += InputReady;
            foreach (Control child in control.Controls) Watch(child);
        }
        void QueueFocus()
        {
            if (finished || queued || host.IsDisposed || !host.IsHandleCreated) return;
            queued = true;
            host.BeginInvoke(new Action(() => {
                queued = false;
                if (finished) return;
                if (host.IsDisposed || scope.IsDisposed || !isCurrent()) { StopWaiting(); return; }
                // A user who moved into an input while initial lookups were loading keeps it.
                if (waiting.Count > 0 && HasInputFocus(scope)) { StopWaiting(); return; }
                if (FocusFirstInput(scope)) { StopWaiting(); return; }
                if (waiting.Count == 0)
                {
                    Watch(scope);
                    scope.Disposed += Abandon;
                }
            }));
        }
        if (host.IsHandleCreated) QueueFocus();
        else
        {
            void Ready(object? sender, EventArgs e)
            {
                host.HandleCreated -= Ready;
                host.Disposed -= Cancel;
                QueueFocus();
            }
            void Cancel(object? sender, EventArgs e)
            {
                host.HandleCreated -= Ready;
                host.Disposed -= Cancel;
            }
            host.HandleCreated += Ready;
            host.Disposed += Cancel;
        }
    }
    internal static bool HasInputFocus(Control scope)
    {
        if (!scope.ContainsFocus) return false;
        if (scope is ModernTextBox or ModernComboBox or ModernDatePicker or TextBoxBase or ComboBox or DateTimePicker or NumericUpDown)
            return true;
        return scope.Controls.Cast<Control>().Any(HasInputFocus);
    }
    internal static bool FocusFirstInput(Control scope)
    {
        if (!scope.Visible || !scope.Enabled || scope.IsDisposed) return false;
        if (scope is V2Page page) return page.FocusEntryField();
        if (scope is ModernTextBox or ModernComboBox or ModernDatePicker or TextBoxBase or ComboBox or DateTimePicker or NumericUpDown)
            return MasterEditorFocusNavigator.FocusInside(scope);
        foreach (Control child in scope.Controls.Cast<Control>().OrderBy(c => c.TabIndex))
            if (FocusFirstInput(child)) return true;
        return false;
    }
    private static readonly ConditionalWeakTable<Control, object> OptedIn = new();
    /// <summary>Call once, after the data-entry scope's controls are built.</summary>
    public static void EnterMovesToNext(Control scope)
    {
        if (!OptedIn.TryGetValue(scope, out _))
            OptedIn.Add(scope, new object());
    }
    /// <summary>Make table-based entry groups follow their visible rows, then columns,
    /// regardless of construction order. Other containers keep their explicit order.</summary>
    public static void OrderTableFields(Control scope)
    {
        if (scope is TableLayoutPanel table)
        {
            var ordered = table.Controls.Cast<Control>()
                .OrderBy(control => table.GetPositionFromControl(control).Row)
                .ThenBy(control => table.GetPositionFromControl(control).Column)
                .ToArray();
            for (int index = 0; index < ordered.Length; index++)
                ordered[index].TabIndex = index;
        }
        foreach (Control child in scope.Controls)
            OrderTableFields(child);
    }
    /// <summary>Called from the inner native controls' ProcessCmdKey when Enter is pressed. Returns true when the key was consumed by moving focus.</summary>
    internal static bool TryAdvance(Control inner)
    {
        Control? scope = inner;
        while (scope != null && !OptedIn.TryGetValue(scope, out _))
            scope = scope.Parent;
        if (scope == null)
            return false;
        Control? next = inner;
        for (int guard = 0; guard < 200; guard++)
        {
            next = scope.GetNextControl(next, true);
            if (next == null || next == inner)
                return false;
            if (!next.Visible || !next.Enabled || !next.TabStop)
                continue;
            // Wrapped inputs are panels; focus their native child, not the decorative shell.
            if (next is Panel || !next.CanSelect)
                continue;
            /* Ticks and radios are chosen deliberately (Space / arrows), they are not typed into, so Enter passes over them to the next field that is. */
            if (next is CheckBox or RadioButton)
                continue;
            // Reaching an action focuses it. A separate Enter activates it, so
            // finishing a field cannot also submit the form.
            if (!next.Focus())
                continue;
            if (next is TextBoxBase text && !text.Multiline)
                text.SelectAll();
            return true;
        }
        return false;
    }
}
