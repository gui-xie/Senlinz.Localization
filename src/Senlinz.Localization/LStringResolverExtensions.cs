namespace Senlinz.Localization;

/// <summary>
/// Extension methods for <see cref="LStringResolver"/>.
/// </summary>
public static class LStringResolverExtensions
{
    /// <summary>
    /// Resolves a localizable string.
    /// </summary>
    public static string Resolve(this LStringResolver resolver, LString lString) => resolver[lString];
}
