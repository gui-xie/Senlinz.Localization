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
    /// Falls back to <see cref="DefaultValue"/> when the resolver returns <see langword="null"/>.
    /// </summary>
    public string Resolve(Func<string, string?>? resolve = null)
    {
        var value = resolve?.Invoke(Key) ?? DefaultValue;
        if (_arguments.Length == 0 || string.IsNullOrEmpty(value))
        {
            return value;
        }

        var replacements = new Dictionary<string, string>(_arguments.Length, StringComparer.Ordinal);
        foreach (var argument in _arguments)
        {
            replacements[argument.Key] = argument.Value;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        var startIndex = 0;
        while (startIndex < value.Length)
        {
            var openBraceIndex = value.IndexOf('{', startIndex);
            if (openBraceIndex < 0)
            {
                builder.Append(value, startIndex, value.Length - startIndex);
                break;
            }

            var closeBraceIndex = value.IndexOf('}', openBraceIndex + 1);
            if (closeBraceIndex < 0)
            {
                builder.Append(value, startIndex, value.Length - startIndex);
                break;
            }

            builder.Append(value, startIndex, openBraceIndex - startIndex);

            var token = value.Substring(openBraceIndex + 1, closeBraceIndex - openBraceIndex - 1);
            if (token.Length > 0 && token.IndexOf('{') < 0 && replacements.TryGetValue(token, out var replacement))
            {
                builder.Append(replacement);
            }
            else
            {
                builder.Append(value, openBraceIndex, closeBraceIndex - openBraceIndex + 1);
            }

            startIndex = closeBraceIndex + 1;
        }

        return builder.ToString();
    }
}
}
