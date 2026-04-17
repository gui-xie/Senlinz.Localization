namespace Senlinz.Localization
{

/// <summary>
/// Represents a localizable string.
/// </summary>
public readonly struct LString
{
    private readonly KeyValuePair<string, string>[] _arguments;

    /// <summary>
    /// Represents an empty localizable string.
    /// </summary>
    public static readonly LString Empty = new LString(string.Empty, string.Empty);

    /// <summary>
    /// Creates a new <see cref="LString"/>.
    /// </summary>
    public LString(string key, string defaultValue, params KeyValuePair<string, string>[] arguments)
    {
        Key = key;
        DefaultValue = defaultValue;
        _arguments = arguments ?? Array.Empty<KeyValuePair<string, string>>();
    }

    /// <summary>
    /// Gets the localization key.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the fallback value.
    /// </summary>
    public string DefaultValue { get; }

    /// <summary>
    /// Resolves the string with an optional key resolver.
    /// </summary>
    public string Resolve(Func<string, string>? resolve = null)
    {
        var value = resolve?.Invoke(Key) ?? DefaultValue;
        if (string.IsNullOrWhiteSpace(value))
        {
            value = DefaultValue;
        }

        foreach (var argument in _arguments)
        {
            value = value.Replace($"{{{argument.Key}}}", argument.Value);
        }

        return value;
    }
}
}
