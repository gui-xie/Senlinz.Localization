using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

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

    /// <summary>
    /// Creates a resolver that uses the provided resources directly.
    /// </summary>
    public static LStringResolver Create(GetCulture getCulture, params ILResource[] resources) =>
        new(getCulture, CreateCultureResource(resources));

    /// <summary>
    /// Creates a resolver that uses generated resources discovered from loaded assemblies.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LStringResolver Create(GetCulture getCulture) =>
        Create(getCulture, DefaultResourceFactory.GetDefaultAssembly());

    /// <summary>
    /// Creates a resolver that uses generated resources from the provided assembly.
    /// </summary>
    public static LStringResolver Create(GetCulture getCulture, Assembly assembly)
    {
        if (getCulture is null)
        {
            throw new ArgumentNullException(nameof(getCulture));
        }

        if (assembly is null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        var resources = DefaultResourceFactory.CreateAll(assembly);
        return new LStringResolver(getCulture, CreateCultureResource(resources));
    }

    internal static GetCultureResource CreateCultureResource(params ILResource[] resources)
    {
        if (resources is null)
        {
            throw new ArgumentNullException(nameof(resources));
        }

        var resourceMap = new Dictionary<string, ILResource>(resources.Length);
        for (var index = 0; index < resources.Length; index++)
        {
            var resource = resources[index] ?? throw new ArgumentNullException(nameof(resources), $"Resource at index {index} is null.");
            if (resourceMap.ContainsKey(resource.Culture))
            {
                throw new ArgumentException($"Duplicate resource culture '{resource.Culture}' was provided.", nameof(resources));
            }

            resourceMap.Add(resource.Culture, resource);
        }

        return culture =>
        {
            resourceMap.TryGetValue(culture, out var resource);
            return resource?.GetResource() ?? new Dictionary<string, string>();
        };
    }
}

/// <summary>
/// Resolves <see cref="LString"/> values for a specific marker type.
/// </summary>
public sealed class LStringResolver<T>(GetCulture getCulture, GetCultureResource getCultureResource)
    : LStringResolver(getCulture, getCultureResource), ILStringResolver<T>
{
    /// <summary>
    /// Creates a resolver for a specific marker type that uses the provided resources directly.
    /// </summary>
    public static new LStringResolver<T> Create(GetCulture getCulture, params ILResource[] resources) =>
        new(getCulture, CreateCultureResource(resources));

    /// <summary>
    /// Creates a resolver that uses generated resources from the marker type assembly.
    /// </summary>
    public static new LStringResolver<T> Create(GetCulture getCulture)
    {
        if (getCulture is null)
        {
            throw new ArgumentNullException(nameof(getCulture));
        }

        var resources = DefaultResourceFactory.CreateAll(typeof(T).Assembly);
        return new LStringResolver<T>(getCulture, CreateCultureResource(resources));
    }
}

internal static class DefaultResourceFactory
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Assembly GetDefaultAssembly()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var candidates = assemblies
            .Where(static assembly => HasDefaultResource(assembly))
            .ToArray();

        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException("No generated primary localization resource was found in the current application domain."),
            _ => throw new InvalidOperationException("Multiple generated primary localization resources were found. Use LStringResolver<T>.Create with a marker type from the desired localization namespace."),
        };
    }

    public static ILResource[] CreateAll(Assembly assembly)
    {
        var candidates = GetGeneratedResourceTypes(assembly);

        if (candidates.Length == 0)
        {
            throw new InvalidOperationException($"No generated localization resources were found in assembly '{assembly.FullName}'.");
        }

        return candidates
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Select(CreateInstance)
            .ToArray();
    }

    private static bool HasDefaultResource(Assembly assembly) => GetDefaultResourceTypes(assembly).Length > 0;

    private static Type[] GetDefaultResourceTypes(Assembly assembly) =>
        assembly
            .GetTypes()
            .Where(static type =>
                type is { IsAbstract: false, IsClass: true } &&
                typeof(IDefaultLResource).IsAssignableFrom(type) &&
                typeof(ILResource).IsAssignableFrom(type))
            .ToArray();

    private static Type[] GetGeneratedResourceTypes(Assembly assembly) =>
        assembly
            .GetTypes()
            .Where(static type =>
                type is { IsAbstract: false, IsClass: true } &&
                typeof(IGeneratedLResource).IsAssignableFrom(type) &&
                typeof(ILResource).IsAssignableFrom(type))
            .ToArray();

    private static ILResource CreateInstance(Type type) =>
        Activator.CreateInstance(type, nonPublic: true) as ILResource
        ?? throw new InvalidOperationException($"Unable to create generated localization resource '{type.FullName}'.");
}
