namespace FMSC_SMS.Core;

/// <summary>Implemented by inputs that can draw the shared validation state.</summary>
public interface IValidationTarget
{
    bool IsInvalid { get; set; }
}
