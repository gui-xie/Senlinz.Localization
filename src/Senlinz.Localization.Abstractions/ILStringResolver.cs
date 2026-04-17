namespace Senlinz.Localization
{

/// <summary>
/// Resolves localized strings.
/// </summary>
public interface ILStringResolver
{
    /// <summary>
    /// Resolves a localized string.
    /// </summary>
    string this[LString lString] { get; }
}

/// <summary>
/// Resolves localized strings for a specific module marker type.
/// </summary>
public interface ILStringResolver<T> : ILStringResolver
{
}
}
