namespace FMSC_SMS.Core;

internal static class PhoneNumberInput
{
    internal static void Attach(ModernTextBox field, Func<FormValidator> validator)
    {
        field.PlaceholderText = "0300-1234567 or +92 300 1234567";
        field.AccessibleDescription = "Optional Pakistan mobile or landline number, including its area code.";
        field.KeyPress += (_, e) => {
            if (!char.IsControl(e.KeyChar) && !(e.KeyChar is >= '0' and <= '9' or '+' or '-' or ' ' or '(' or ')'))
                e.Handled = true;
        };
        void Validate()
        {
            if (field.IsDisposed || field.ReadOnly) return;
            validator().Rule(field, () => PakistanPhoneNumber.IsValidOptional(field.Text),
                PakistanPhoneNumber.ValidationMessage);
        }
        // Pasted invalid values remain visible; never silently strip bad input.
        field.TextChanged += (_, _) => { if (field.Focused) Validate(); };
        field.Leave += (_, _) => Validate();
    }
}
