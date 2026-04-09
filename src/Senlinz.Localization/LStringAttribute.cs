namespace Senlinz.Localization;

/// <summary>
/// Marks an enum for localization key generation.
/// </summary>
[AttributeUsage(AttributeTargets.Enum)]
public sealed class LStringAttribute(string separator = "_") : Attribute
{
    /// <summary>
    /// Gets the separator used when building enum localization keys.
    /// </summary>
    public string Separator { get; } = separator;
}
