using System.Collections.Concurrent;

namespace Senlinz.Localization;

/// <summary>
/// Resolves <see cref="LString"/> values for the active culture.
/// </summary>
public class LStringResolver(GetCulture getCulture, GetCultureResource getCultureResource) : ILStringResolver
{
    private static readonly IReadOnlyDictionary<string, string> EmptyDictionary = new Dictionary<string, string>();
    private readonly ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<string, string>>> _dictionaries = new();

    /// <summary>
    /// Resolves <see cref="LString"/> values by using a resource provider.
    /// </summary>
    public LStringResolver(GetCulture getCulture, LResourceProvider resourceProvider)
        : this(getCulture, resourceProvider.GetResource)
    {
    }

    /// <summary>
    /// Resolves <see cref="LString"/> values by using the provided resources directly.
    /// </summary>
    public LStringResolver(GetCulture getCulture, params ILResource[] resources)
        : this(getCulture, new LResourceProvider(resources))
    {
    }

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
    /// <summary>
    /// Resolves <see cref="LString"/> values for a specific marker type by using a resource provider.
    /// </summary>
    public LStringResolver(GetCulture getCulture, LResourceProvider resourceProvider)
        : this(getCulture, resourceProvider.GetResource)
    {
    }

    /// <summary>
    /// Resolves <see cref="LString"/> values for a specific marker type by using the provided resources directly.
    /// </summary>
    public LStringResolver(GetCulture getCulture, params ILResource[] resources)
        : this(getCulture, new LResourceProvider(resources))
    {
    }
}
