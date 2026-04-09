namespace Senlinz.Localization;

/// <summary>
/// Overrides the localization key generated for an enum field.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class LStringKeyAttribute(string key) : Attribute
{
    /// <summary>
    /// Gets the localization key.
    /// </summary>
    public string Key { get; } = key;
}
