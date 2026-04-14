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
    /// Creates a resolver that uses the generated default resource discovered from loaded assemblies.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LStringResolver CreateDefault(GetCulture getCulture) =>
        CreateDefault(getCulture, DefaultResourceFactory.GetDefaultAssembly());

    /// <summary>
    /// Creates a resolver that uses the generated default resource from the provided assembly.
    /// </summary>
    public static LStringResolver CreateDefault(GetCulture getCulture, Assembly assembly)
    {
        if (getCulture is null)
        {
            throw new ArgumentNullException(nameof(getCulture));
        }

        if (assembly is null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        var resource = DefaultResourceFactory.Create(assembly);
        return new LStringResolver(getCulture, _ => resource.GetResource());
    }

    internal static GetCultureResource CreateCultureResource(params ILResource[] resources)
    {
        var resourceMap = resources.ToDictionary(resource => resource.Culture);
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
    /// Creates a resolver that uses the generated default resource from the marker type assembly.
    /// </summary>
    public static new LStringResolver<T> CreateDefault(GetCulture getCulture)
    {
        if (getCulture is null)
        {
            throw new ArgumentNullException(nameof(getCulture));
        }

        var resource = DefaultResourceFactory.Create(typeof(T).Assembly);
        return new LStringResolver<T>(getCulture, _ => resource.GetResource());
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
            0 => throw new InvalidOperationException("No generated LDefaultResource was found in the current application domain."),
            _ => throw new InvalidOperationException("Multiple generated LDefaultResource types were found. Use LStringResolver<T>.CreateDefault with a marker type from the desired localization namespace."),
        };
    }

    public static ILResource Create(Assembly assembly)
    {
        var candidates = GetDefaultResourceTypes(assembly);

        return candidates.Length switch
        {
            1 => CreateInstance(candidates[0]),
            0 => throw new InvalidOperationException($"No generated LDefaultResource was found in assembly '{assembly.FullName}'."),
            _ => throw new InvalidOperationException($"Multiple generated LDefaultResource types were found in assembly '{assembly.FullName}'. Use LStringResolver<T>.CreateDefault with a marker type from the desired localization namespace."),
        };
    }

    private static bool HasDefaultResource(Assembly assembly) => GetDefaultResourceTypes(assembly).Length > 0;

    private static Type[] GetDefaultResourceTypes(Assembly assembly) =>
        assembly
            .GetTypes()
            .Where(static type =>
                type is { IsAbstract: false, IsClass: true } &&
                type.Name == "LDefaultResource" &&
                typeof(ILResource).IsAssignableFrom(type))
            .ToArray();

    private static ILResource CreateInstance(Type type) =>
        Activator.CreateInstance(type) as ILResource
        ?? throw new InvalidOperationException($"Unable to create generated default resource '{type.FullName}'.");
}
