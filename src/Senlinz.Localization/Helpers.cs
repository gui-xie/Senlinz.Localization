using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Senlinz.Localization
{

public sealed partial class LGenerator
{
    private const int MaxLocalizationDepth = 64;
    private static readonly Regex PlaceholderRegex = new Regex("(?<={)[^{}]+(?=})", RegexOptions.Compiled);
    private static readonly Regex IdentifierWordRegex = new Regex("[A-Z]+(?=[A-Z][a-z]|[0-9]|$)|[A-Z]?[a-z]+|[0-9]+", RegexOptions.Compiled);

    private static LocalizationFileParseResult ParseLocalizationFile(LocalizationFileCandidate candidate)
    {
        var diagnostics = ImmutableArray.CreateBuilder<LocalizationDiagnosticInfo>();
        if (string.IsNullOrWhiteSpace(candidate.JsonText))
        {
            diagnostics.Add(CreateFileDiagnostic(InvalidLocalizationJsonDescriptor, candidate.Path, candidate.FileName, "The file is empty."));
            return new LocalizationFileParseResult(null, diagnostics.ToImmutable());
        }

        try
        {
            using var document = JsonDocument.Parse(candidate.JsonText);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(CreateFileDiagnostic(InvalidLocalizationJsonDescriptor, candidate.Path, candidate.FileName, "The root element must be a JSON object."));
                return new LocalizationFileParseResult(null, diagnostics.ToImmutable());
            }

            var entries = new List<LocalizationEntry>();
            FlattenLocalizationEntries(document.RootElement, new List<string>(), entries);
            if (entries.Count == 0)
            {
                diagnostics.Add(CreateFileDiagnostic(InvalidLocalizationJsonDescriptor, candidate.Path, candidate.FileName, "No string localization entries were found."));
                return new LocalizationFileParseResult(null, diagnostics.ToImmutable());
            }

            var infos = GetLStringInfos(entries).ToImmutableArray();
            AddDuplicateKeyDiagnostics(candidate, entries, diagnostics);
            AddGeneratedIdentifierDiagnostics(candidate, infos, diagnostics);
            if (diagnostics.Count > 0)
            {
                return new LocalizationFileParseResult(null, diagnostics.ToImmutable());
            }

            return new LocalizationFileParseResult(
                new LocalizationFileModel(
                    candidate.Path,
                    candidate.FileName,
                    candidate.CultureName,
                    GetResourceClassName(candidate.CultureName),
                    infos),
                diagnostics.ToImmutable());
        }
        catch (JsonException exception)
        {
            diagnostics.Add(CreateJsonExceptionDiagnostic(candidate, exception));
            return new LocalizationFileParseResult(null, diagnostics.ToImmutable());
        }
    }

    private static void AddDuplicateKeyDiagnostics(
        LocalizationFileCandidate candidate,
        IEnumerable<LocalizationEntry> entries,
        ICollection<LocalizationDiagnosticInfo> diagnostics)
    {
        foreach (var duplicateKey in entries
                     .GroupBy(entry => entry.Key, StringComparer.Ordinal)
                     .Where(group => group.Skip(1).Any())
                     .Select(group => group.Key)
                     .OrderBy(key => key, StringComparer.Ordinal))
        {
            diagnostics.Add(CreateFileDiagnostic(DuplicateLocalizationKeyDescriptor, candidate.Path, candidate.FileName, duplicateKey));
        }
    }

    private static void AddGeneratedIdentifierDiagnostics(
        LocalizationFileCandidate candidate,
        IReadOnlyCollection<LStringInfo> infos,
        ICollection<LocalizationDiagnosticInfo> diagnostics)
    {
        var scopeMembers = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.Ordinal);
        foreach (var info in infos)
        {
            AddScopeMember(scopeMembers, string.Empty, info.KeyProperty, $"leaf:{info.Key}", $"localization key '{info.Key}'");
            if (info.PathSegments.Count <= 1)
            {
                continue;
            }

            var scopeSegments = new List<string>();
            for (var index = 0; index < info.PathSegments.Count - 1; index++)
            {
                var identifier = JsonKeyToIdentifier(info.PathSegments[index]);
                scopeSegments.Add(info.PathSegments[index]);
                AddScopeMember(
                    scopeMembers,
                    index == 0 ? string.Empty : string.Join(".", scopeSegments.Take(index)),
                    identifier,
                    $"node:{string.Join(".", scopeSegments)}",
                    $"nested API path '{string.Join(".", scopeSegments)}'");
            }

            AddScopeMember(
                scopeMembers,
                string.Join(".", info.PathSegments.Take(info.PathSegments.Count - 1)),
                JsonKeyToIdentifier(info.PathSegments[info.PathSegments.Count - 1]),
                $"leaf:{info.Key}",
                $"localization key '{info.Key}'");
        }

        foreach (var scopeMember in scopeMembers.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var scope = scopeMember.Key;
            var members = scopeMember.Value;
            foreach (var member in members.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var identifier = member.Key;
                var conflicts = member.Value;
                if (conflicts.Count <= 1)
                {
                    continue;
                }

                var details = string.Join(", ", conflicts.Values.OrderBy(value => value, StringComparer.Ordinal));
                diagnostics.Add(CreateFileDiagnostic(
                    ConflictingLocalizationIdentifierDescriptor,
                    candidate.Path,
                    candidate.FileName,
                    identifier,
                    $"scope '{GetScopeDisplayName(scope)}' from {details}"));
            }
        }
    }

    private static void AddScopeMember(
        IDictionary<string, Dictionary<string, Dictionary<string, string>>> scopeMembers,
        string scope,
        string identifier,
        string memberKey,
        string displayValue)
    {
        if (!scopeMembers.TryGetValue(scope, out var members))
        {
            members = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            scopeMembers.Add(scope, members);
        }

        if (!members.TryGetValue(identifier, out var conflicts))
        {
            conflicts = new Dictionary<string, string>(StringComparer.Ordinal);
            members.Add(identifier, conflicts);
        }

        conflicts[memberKey] = displayValue;
    }

    private static string GetScopeDisplayName(string scope) =>
        string.IsNullOrWhiteSpace(scope)
            ? "L"
            : $"L.{string.Join(".", scope.Split('.').Select(JsonKeyToIdentifier))}";

    private static LocalizationDiagnosticInfo CreateJsonExceptionDiagnostic(LocalizationFileCandidate candidate, JsonException exception)
    {
        var line = exception.LineNumber is long lineNumber && lineNumber >= 0 ? (int)lineNumber : 0;
        var character = exception.BytePositionInLine is long position && position >= 0 ? (int)position : 0;
        return new LocalizationDiagnosticInfo(
            InvalidLocalizationJsonDescriptor,
            candidate.Path,
            line,
            character,
            candidate.FileName,
            exception.Message);
    }

    private static LocalizationDiagnosticInfo CreateFileDiagnostic(
        DiagnosticDescriptor descriptor,
        string filePath,
        params object[] messageArguments) =>
        new LocalizationDiagnosticInfo(
            descriptor,
            filePath,
            0,
            0,
            messageArguments);

    private static void FlattenLocalizationEntries(JsonElement element, IReadOnlyList<string> pathSegments, List<LocalizationEntry> entries)
    {
        var stack = new Stack<(JsonElement Element, string[] PathSegments)>();
        stack.Push((element, pathSegments.Count == 0 ? Array.Empty<string>() : pathSegments.ToArray()));

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            var properties = current.Element.EnumerateObject().ToArray();
            for (var index = properties.Length - 1; index >= 0; index--)
            {
                var property = properties[index];
                var nextPathSegments = new string[current.PathSegments.Length + 1];
                Array.Copy(current.PathSegments, nextPathSegments, current.PathSegments.Length);
                nextPathSegments[current.PathSegments.Length] = property.Name;
                if (nextPathSegments.Length > MaxLocalizationDepth)
                {
                    throw new JsonException($"The localization nesting depth exceeds the supported limit of {MaxLocalizationDepth}.");
                }

                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    stack.Push((property.Value, nextPathSegments));
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    entries.Add(new LocalizationEntry(
                        GetFlattenedLocalizationKey(nextPathSegments),
                        property.Value.GetString() ?? string.Empty,
                        nextPathSegments));
                }
            }
        }
    }

    private static string GetFlattenedLocalizationKey(IReadOnlyList<string> pathSegments)
    {
        if (pathSegments.Count == 0)
        {
            return "_";
        }

        if (pathSegments.Count == 1)
        {
            return pathSegments[0];
        }

        return string.Join(".", pathSegments);
    }

    private static string GetResourceClassName(string cultureName)
    {
        var builder = new StringBuilder();
        foreach (var segment in Regex.Split(cultureName, "[^A-Za-z0-9_]+"))
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            var identifier = JsonKeyToIdentifier(segment);
            if (string.IsNullOrWhiteSpace(identifier) || identifier == "_")
            {
                continue;
            }

            builder.Append(identifier);
        }

        if (builder.Length == 0)
        {
            builder.Append("Localization");
        }

        return $"{builder}Resource";
    }

    private static void AppendNamespaceStart(StringBuilder source, string targetNamespace)
    {
        source.AppendLine();
        if (string.IsNullOrWhiteSpace(targetNamespace))
        {
            return;
        }

        source.AppendLine($"namespace {targetNamespace}");
        source.AppendLine("{");
    }

    private static void AppendNamespaceEnd(StringBuilder source, string targetNamespace)
    {
        if (!string.IsNullOrWhiteSpace(targetNamespace))
        {
            source.AppendLine("}");
        }
    }

    private static string ResolveTargetNamespace(string? assemblyName, string? rootNamespace)
    {
        var candidate = string.IsNullOrWhiteSpace(rootNamespace) ? assemblyName : rootNamespace;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        var segments = candidate!.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(".", segments.Select(SanitizeNamespaceIdentifier));
    }

    private static string SanitizeNamespaceIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "_";
        }

        if (SyntaxFacts.IsValidIdentifier(value))
        {
            return value;
        }

        var builder = new StringBuilder();
        foreach (var character in value)
        {
            if (builder.Length == 0)
            {
                if (character == '_' || char.IsLetter(character))
                {
                    builder.Append(character);
                    continue;
                }

                if (char.IsDigit(character))
                {
                    builder.Append('_');
                    builder.Append(character);
                    continue;
                }

                builder.Append('_');
                continue;
            }

            builder.Append(character == '_' || char.IsLetterOrDigit(character) ? character : '_');
        }

        var identifier = builder.Length == 0 ? "_" : builder.ToString();
        return SyntaxFacts.IsValidIdentifier(identifier) ? identifier : $"_{identifier}";
    }

    private static string GetNamespace(BaseTypeDeclarationSyntax syntax)
    {
        SyntaxNode? parent = syntax.Parent;
        while (parent != null && !(parent is BaseNamespaceDeclarationSyntax))
        {
            parent = parent.Parent;
        }

        return parent is BaseNamespaceDeclarationSyntax namespaceSyntax
            ? namespaceSyntax.Name.ToString()
            : string.Empty;
    }

    private static List<LStringInfo> GetLStringInfos(IReadOnlyCollection<LocalizationEntry> entries)
    {
        var result = new List<LStringInfo>();
        foreach (var entry in entries)
        {
            if (entry.Key is null || entry.Value is null)
            {
                continue;
            }

            var parameters = new List<LStringParameter>();
            var value = entry.Value;
            foreach (Match match in PlaceholderRegex.Matches(value))
            {
                if (match.Value.StartsWith("$", StringComparison.Ordinal))
                {
                    value = value.Replace(match.Value, match.Value.Substring(1));
                    continue;
                }

                if (parameters.Any(parameter => parameter.Token == match.Value))
                {
                    continue;
                }

                parameters.Add(new LStringParameter(match.Value, EnsureUniqueParameterName(parameters, ToCamelCase(JsonKeyToIdentifier(match.Value)))));
            }

            var keyProperty = JsonKeyToIdentifier(entry.Key);
            result.Add(new LStringInfo(
                entry.Key,
                value,
                keyProperty,
                entry.PathSegments,
                parameters));
        }

        return result;
    }

    private static string EnsureUniqueParameterName(IEnumerable<LStringParameter> existingParameters, string candidate)
    {
        var baseName = string.IsNullOrWhiteSpace(candidate) ? "value" : candidate;
        var parameterName = baseName;
        var suffix = 1;
        while (existingParameters.Any(parameter => parameter.ParameterName == parameterName))
        {
            suffix++;
            parameterName = $"{baseName}{suffix}";
        }

        return parameterName;
    }

    private static string JsonKeyToIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "_";
        }

        var words = Regex.Split(value, "[^A-Za-z0-9]+")
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .SelectMany(segment => IdentifierWordRegex.Matches(segment).Cast<Match>().Select(match => match.Value))
            .ToArray();
        if (words.Length == 0)
        {
            return "_";
        }

        var builder = new StringBuilder();
        if (char.IsDigit(words[0][0]))
        {
            builder.Append('_');
        }

        foreach (var word in words)
        {
            if (char.IsDigit(word[0]))
            {
                builder.Append(word);
                continue;
            }

            builder.Append(char.ToUpperInvariant(word[0]));
            if (word.Length > 1)
            {
                builder.Append(word.Substring(1).ToLowerInvariant());
            }
        }

        var identifier = builder.Length == 0 ? "_" : builder.ToString();
        return SyntaxFacts.IsValidIdentifier(identifier) ? identifier : $"_{identifier}";
    }

    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "value";
        }

        return value.Length == 1
            ? char.ToLowerInvariant(value[0]).ToString()
            : char.ToLowerInvariant(value[0]) + value.Substring(1);
    }

    private static string ToLiteral(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\0':
                    builder.Append("\\0");
                    break;
                case '\a':
                    builder.Append("\\a");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\v':
                    builder.Append("\\v");
                    break;
                default:
                    if (char.IsControl(character) || character == '\u2028' || character == '\u2029')
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        break;
                    }

                    builder.Append(character);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static string EscapeXml(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            switch (character)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '"':
                    builder.Append("&quot;");
                    break;
                case '\'':
                    builder.Append("&apos;");
                    break;
                default:
                    if (XmlConvert.IsXmlChar(character))
                    {
                        builder.Append(character);
                    }
                    else
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static void AppendSummary(StringBuilder source, string indent, string value)
    {
        source.AppendLine($"{indent}/// <summary>");
        foreach (var line in value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            source.AppendLine(string.IsNullOrEmpty(line)
                ? $"{indent}///"
                : $"{indent}/// {EscapeXml(line)}");
        }

        source.AppendLine($"{indent}/// </summary>");
    }

    private sealed class LStringInfo
    {
        public LStringInfo(
            string key,
            string defaultValue,
            string keyProperty,
            IReadOnlyList<string> pathSegments,
            IReadOnlyList<LStringParameter> parameters)
        {
            Key = key;
            DefaultValue = defaultValue;
            KeyProperty = keyProperty;
            PathSegments = pathSegments;
            Parameters = parameters;
        }

        public string Key { get; }

        public string DefaultValue { get; }

        public string KeyProperty { get; }

        public IReadOnlyList<string> PathSegments { get; }

        public IReadOnlyList<LStringParameter> Parameters { get; }
    }

    private sealed class LocalizationEntry
    {
        public LocalizationEntry(string key, string value, IReadOnlyList<string> pathSegments)
        {
            Key = key;
            Value = value;
            PathSegments = pathSegments;
        }

        public string Key { get; }

        public string Value { get; }

        public IReadOnlyList<string> PathSegments { get; }
    }

    private sealed class LocalizationFileCandidate
    {
        public LocalizationFileCandidate(string path, string fileName, string cultureName, string jsonText)
        {
            Path = path;
            FileName = fileName;
            CultureName = cultureName;
            JsonText = jsonText;
        }

        public string Path { get; }

        public string FileName { get; }

        public string CultureName { get; }

        public string JsonText { get; }
    }

    private sealed class LocalizationFileParseResult
    {
        public LocalizationFileParseResult(LocalizationFileModel? file, ImmutableArray<LocalizationDiagnosticInfo> diagnostics)
        {
            File = file;
            Diagnostics = diagnostics;
        }

        public LocalizationFileModel? File { get; }

        public ImmutableArray<LocalizationDiagnosticInfo> Diagnostics { get; }
    }

    private sealed class LocalizationFileModel
    {
        public LocalizationFileModel(
            string path,
            string fileName,
            string cultureName,
            string resourceClassName,
            ImmutableArray<LStringInfo> infos)
        {
            Path = path;
            FileName = fileName;
            CultureName = cultureName;
            ResourceClassName = resourceClassName;
            Infos = infos;
        }

        public string Path { get; }

        public string FileName { get; }

        public string CultureName { get; }

        public string ResourceClassName { get; }

        public ImmutableArray<LStringInfo> Infos { get; }
    }

    private sealed class LocalizationGenerationState
    {
        public LocalizationGenerationState(
            string targetNamespace,
            ImmutableArray<LocalizationFileModel> files,
            LocalizationFileModel? primaryFile,
            ImmutableArray<LocalizationDiagnosticInfo> diagnostics)
        {
            TargetNamespace = targetNamespace;
            Files = files;
            PrimaryFile = primaryFile;
            Diagnostics = diagnostics;
        }

        public string TargetNamespace { get; }

        public ImmutableArray<LocalizationFileModel> Files { get; }

        public LocalizationFileModel? PrimaryFile { get; }

        public ImmutableArray<LocalizationDiagnosticInfo> Diagnostics { get; }
    }

    private sealed class LocalizationDiagnosticInfo
    {
        public LocalizationDiagnosticInfo(
            DiagnosticDescriptor descriptor,
            string filePath,
            int line,
            int character,
            params object[] messageArguments)
        {
            Descriptor = descriptor;
            FilePath = filePath;
            Line = line;
            Character = character;
            MessageArguments = messageArguments;
        }

        public DiagnosticDescriptor Descriptor { get; }

        public string FilePath { get; }

        public int Line { get; }

        public int Character { get; }

        public object[] MessageArguments { get; }

        public Diagnostic ToDiagnostic()
        {
            var position = new LinePosition(Line, Character);
            return Diagnostic.Create(
                Descriptor,
                Location.Create(FilePath, new TextSpan(0, 0), new LinePositionSpan(position, position)),
                MessageArguments);
        }
    }

    private sealed class NestedLApiNode
    {
        public NestedLApiNode(string identifier, string typeName)
        {
            Identifier = identifier;
            TypeName = typeName;
        }

        public string Identifier { get; }

        public string TypeName { get; }

        public bool IsRoot => Identifier.Length == 0;

        public Dictionary<string, NestedLApiNode> Children { get; } = new Dictionary<string, NestedLApiNode>(StringComparer.Ordinal);

        public List<NestedLApiLeaf> Leaves { get; } = new List<NestedLApiLeaf>();

        public NestedLApiNode GetOrAddChild(string identifier)
        {
            if (Children.TryGetValue(identifier, out var child))
            {
                return child;
            }

            child = new NestedLApiNode(identifier, $"{identifier}Node");
            Children.Add(identifier, child);
            return child;
        }
    }

    private sealed class NestedLApiLeaf
    {
        public NestedLApiLeaf(string identifier, LStringInfo info)
        {
            Identifier = identifier;
            Info = info;
        }

        public string Identifier { get; }

        public LStringInfo Info { get; }
    }

    private sealed class LStringParameter
    {
        public LStringParameter(string token, string parameterName)
        {
            Token = token;
            ParameterName = parameterName;
        }

        public string Token { get; }

        public string ParameterName { get; }
    }
}
}
