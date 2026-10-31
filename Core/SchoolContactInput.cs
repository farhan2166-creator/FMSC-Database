namespace FMSC_SMS.Core;

internal static class SchoolContactInput
{
    internal static void AttachPhone(ModernTextBox field, Func<FormValidator> validator)
    {
        field.PlaceholderText = "0300-1234567, 042-31234567";
        field.AccessibleDescription = SchoolContactValidation.PhoneMessage;
        field.KeyPress += (_, e) => {
            if (!char.IsControl(e.KeyChar) && !SchoolContactValidation.IsPhoneCharacter(e.KeyChar))
                e.Handled = true;
        };
        AttachValidation(field, validator, () => SchoolContactValidation.IsValidPhoneList(field.Text), SchoolContactValidation.PhoneMessage);
    }

    internal static void AttachEmail(ModernTextBox field, Func<FormValidator> validator)
    {
        field.PlaceholderText = "name@example.com";
        field.KeyPress += (_, e) => {
            if (!char.IsControl(e.KeyChar) && (char.IsWhiteSpace(e.KeyChar) || e.KeyChar is ',' or ';' or '<' or '>'))
                e.Handled = true;
        };
        AttachValidation(field, validator, () => EmailAddressValidator.IsBlankOrValid(field.Text), SchoolContactValidation.EmailMessage);
    }

    private static void AttachValidation(ModernTextBox field, Func<FormValidator> validator, Func<bool> valid, string message)
    {
        void Validate()
        {
            if (!field.IsDisposed && !field.ReadOnly) validator().Rule(field, valid, message);
        }
        // Keep pasted invalid input visible and refuse it at Save, rather than silently changing it.
        field.TextChanged += (_, _) => { if (field.Focused) Validate(); };
        field.Leave += (_, _) => Validate();
    }
}
