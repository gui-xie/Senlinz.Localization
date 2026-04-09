namespace Senlinz.Localization;

/// <summary>
/// Stores localized resources by culture.
/// </summary>
public sealed class LResourceProvider
{
    private readonly Dictionary<string, ILResource> _resources;

    /// <summary>
    /// Creates a resource provider.
    /// </summary>
    public LResourceProvider(params ILResource[] resources)
    {
        _resources = resources.ToDictionary(resource => resource.Culture);
    }

    /// <summary>
    /// Gets resources for the requested culture.
    /// </summary>
    public Dictionary<string, string> GetResource(string culture)
    {
        _resources.TryGetValue(culture, out var resource);
        return resource?.GetResource() ?? new Dictionary<string, string>();
    }
}
