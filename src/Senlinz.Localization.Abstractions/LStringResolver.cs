using System.Collections.Concurrent;

namespace Senlinz.Localization;

/// <summary>
/// Resolves <see cref="LString"/> values for the active culture.
/// </summary>
public class LStringResolver(GetCulture getCulture, GetCultureResource getCultureResource) : ILStringResolver
{
    private static readonly IReadOnlyDictionary<string, string> EmptyDictionary = new Dictionary<string, string>();
    private readonly ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<string, string>>> _dictionaries = new();

    private string ResolveCore(string key)
    {
        var culture = getCulture();
        var dictionary = _dictionaries.GetOrAdd(
            culture,
            _ => new Lazy<IReadOnlyDictionary<string, string>>(() => getCultureResource(culture) ?? EmptyDictionary));

        if (dictionary.Value.TryGetValue(key, out var value))
        {
            return value;
        }

        return string.Empty;
    }

    /// <summary>
    /// Resolves a localizable string.
    /// </summary>
    public string this[LString lString] => lString.Resolve(ResolveCore);
}

/// <summary>
/// Resolves <see cref="LString"/> values for a specific marker type.
/// </summary>
public sealed class LStringResolver<T>(GetCulture getCulture, GetCultureResource getCultureResource)
    : LStringResolver(getCulture, getCultureResource), ILStringResolver<T>
{
}
