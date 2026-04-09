namespace Senlinz.Localization;

/// <summary>
/// Localized resource provider contract.
/// </summary>
public interface ILResource
{
    /// <summary>
    /// Gets the culture name for the resource.
    /// </summary>
    string Culture { get; }

    /// <summary>
    /// Gets the localized values for the resource.
    /// </summary>
    Dictionary<string, string> GetResource();
}
