namespace Senlinz.Localization
{

/// <summary>
/// Overrides the enum member portion of the generated localization key.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class LStringKeyAttribute : Attribute
{
    /// <summary>
    /// Creates a new <see cref="LStringKeyAttribute"/>.
    /// </summary>
    public LStringKeyAttribute(string key)
    {
        Key = key;
    }

    /// <summary>
    /// Gets the localization key.
    /// </summary>
    public string Key { get; }
}
}
