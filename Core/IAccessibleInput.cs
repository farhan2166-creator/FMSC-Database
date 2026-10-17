namespace FMSC_SMS.Core;

/// <summary>Implemented by custom inputs that wrap a native accessible control.</summary>
public interface IAccessibleInput
{
    void ApplyAccessibleInfo(string? name, string? description);
}
