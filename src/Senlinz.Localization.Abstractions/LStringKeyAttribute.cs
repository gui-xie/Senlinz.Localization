namespace Senlinz.Localization;

/// <summary>
/// Overrides the enum member portion of the generated localization key.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class LStringKeyAttribute(string key) : Attribute
{
    /// <summary>
    /// Gets the localization key.
    /// </summary>
    public string Key { get; } = key;
}
