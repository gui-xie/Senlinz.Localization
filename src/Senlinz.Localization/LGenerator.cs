using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Senlinz.Localization;

/// <summary>
/// Generates localization helpers from JSON files and enum attributes.
/// </summary>
[Generator]
public sealed class LGenerator : IIncrementalGenerator
{
    private static readonly AssemblyName ExecutingAssembly = Assembly.GetExecutingAssembly().GetName();
    private const string LocalizationFileProperty = "build_property.SenlinzLocalizationFile";
    private const string RootNamespaceProperty = "build_property.RootNamespace";
    private const string LStringAttributeName = "Senlinz.Localization.LStringAttribute";
    private const string LStringKeyAttributeSuffix = "LStringKey";
    private const string LStringAttributePrefix = "LString";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        AddJsonLocalizationSource(context);
        AddEnumAttributeSource(context);
    }

    private static void AddJsonLocalizationSource(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(GetLocalizationFileProvider(context), (sourceContext, pair) =>
        {
            var file = pair.Source.File;
            var targetNamespace = pair.Source.TargetNamespace;
            var configuredFileName = pair.FileName;

            if (targetNamespace is null || !file.Path.EndsWith(configuredFileName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var jsonText = file.GetText()?.ToString() ?? string.Empty;
            if (!TryParseLocalizationEntries(jsonText, out var entries))
            {
                return;
            }

            var infos = GetLStringInfos(entries);
            CreateLSource(sourceContext, targetNamespace, infos);
            CreateLResourceSource(sourceContext, targetNamespace, infos);
            CreateDynamicResolverInterface(sourceContext, targetNamespace);
            CreateGlobalAliasesSource(sourceContext, targetNamespace);
        });
    }

    private static void CreateDynamicResolverInterface(SourceProductionContext context, string targetNamespace)
    {
        var source = new StringBuilder();
        source.AppendLine("#nullable enable");
        source.AppendLine("using Senlinz.Localization;");
        AppendNamespaceStart(source, targetNamespace);
        source.AppendLine("    /// <summary>");
        source.AppendLine("    /// Typed localization resolver contract.");
        source.AppendLine("    /// </summary>");
        source.AppendLine("    public interface IL : ILStringResolver");
        source.AppendLine("    {");
        source.AppendLine("    }");
        AppendNamespaceEnd(source, targetNamespace);
        source.Append("#nullable restore");
        context.AddSource("IL.g.cs", source.ToString());
    }

    private static void AddEnumAttributeSource(IncrementalGeneratorInitializationContext context)
    {
        var localizationInfosProvider = GetLocalizationFileProvider(context)
            .Select(static (pair, _) =>
            {
                var file = pair.Source.File;
                var targetNamespace = pair.Source.TargetNamespace;
                var configuredFileName = pair.FileName;

                if (targetNamespace is null || !file.Path.EndsWith(configuredFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return ImmutableArray<LStringInfo>.Empty;
                }

                var jsonText = file.GetText()?.ToString() ?? string.Empty;
                return TryParseLocalizationEntries(jsonText, out var entries)
                    ? GetLStringInfos(entries).ToImmutableArray()
                    : ImmutableArray<LStringInfo>.Empty;
            });

        var enumProviders = context.SyntaxProvider.ForAttributeWithMetadataName(
                LStringAttributeName,
                static (node, _) => node is EnumDeclarationSyntax,
                static (syntaxContext, _) => (Symbol: (INamedTypeSymbol)syntaxContext.TargetSymbol, Syntax: (EnumDeclarationSyntax)syntaxContext.TargetNode));

        context.RegisterSourceOutput(enumProviders.Combine(localizationInfosProvider.Collect()), (sourceContext, pair) =>
        {
            var enumInfo = pair.Left;
            var enumSymbol = enumInfo.Symbol;
            var enumSyntax = enumInfo.Syntax;
            var enumFields = enumSyntax.Members;
            var infos = pair.Right.SelectMany(static item => item).ToArray();
            if (enumFields.Count == 0)
            {
                return;
            }

            var enumName = enumSymbol.Name;
            var enumNamespace = GetNamespace(enumSyntax);
            var enumParameterName = ToCamelCase(enumName);
            var separator = GetSeparator(enumSyntax);
            var className = $"{enumName}Extensions";
            var source = new StringBuilder();
            source.AppendLine("#nullable enable");
            source.AppendLine("using Senlinz.Localization;");
            AppendNamespaceStart(source, enumNamespace);
            source.AppendLine("    /// <summary>");
            source.AppendLine($"    /// {enumName} localization helpers.");
            source.AppendLine("    /// </summary>");
            source.AppendLine($"    [System.CodeDom.Compiler.GeneratedCodeAttribute(\"{ExecutingAssembly.Name}\", \"{ExecutingAssembly.Version}\")]");
            source.AppendLine($"    public static partial class {className}");
            source.AppendLine("    {");
            source.AppendLine("        /// <summary>");
            source.AppendLine($"        /// Converts a {enumName} value to an <see cref=\"LString\"/>.");
            source.AppendLine("        /// </summary>");
            source.AppendLine($"        public static LString ToLString(this {enumName} {enumParameterName})");
            source.AppendLine("        {");
            source.AppendLine($"            return {enumParameterName} switch");
            source.AppendLine("            {");
            foreach (var enumField in enumFields)
            {
                var lAccessExpression = ResolveEnumLAccessExpression(enumName, separator, enumField, infos);
                source.AppendLine($"                {enumName}.{enumField.Identifier.Text} => {lAccessExpression},");
            }

            source.AppendLine("                _ => LString.Empty");
            source.AppendLine("            };");
            source.AppendLine("        }");
            source.AppendLine("    }");
            AppendNamespaceEnd(source, enumNamespace);
            source.Append("#nullable restore");
            sourceContext.AddSource($"{className}.g.cs", source.ToString());
        });
    }

    private static string ResolveEnumLAccessExpression(
        string enumName,
        string separator,
        EnumMemberDeclarationSyntax enumField,
        IReadOnlyCollection<LStringInfo> infos)
    {
        var generatedKey = $"{enumName}{separator}{enumField.Identifier.Text}";
        var enumKeyPrefix = $"{enumName}{separator}";
        var memberKey = enumField.Identifier.Text;
        string? explicitKey = null;
        foreach (var attributeList in enumField.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (!attribute.Name.ToString().EndsWith(LStringKeyAttributeSuffix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression.ToString().Trim('"') is string attributeValue
                    && !string.IsNullOrWhiteSpace(attributeValue))
                {
                    explicitKey = attributeValue;
                    memberKey = attributeValue;
                    generatedKey = attributeValue.StartsWith(enumKeyPrefix, StringComparison.Ordinal)
                        ? attributeValue
                        : $"{enumKeyPrefix}{attributeValue}";
                }
            }
        }

        if (TryGetLAccessExpressionForKey(infos, explicitKey, out var expression)
            || TryGetLAccessExpressionForKey(infos, generatedKey, out expression)
            || TryGetLAccessExpressionForEnumPath(infos, enumName, memberKey, out expression))
        {
            return expression;
        }

        return $"L.{JsonKeyToIdentifier(generatedKey)}";
    }

    private static bool TryGetLAccessExpressionForKey(
        IEnumerable<LStringInfo> infos,
        string? key,
        out string expression)
    {
        expression = string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var info = infos.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
        if (info is null)
        {
            return false;
        }

        expression = $"L.{GetLAccessPath(info)}";
        return true;
    }

    private static bool TryGetLAccessExpressionForEnumPath(
        IEnumerable<LStringInfo> infos,
        string enumName,
        string memberKey,
        out string expression)
    {
        expression = string.Empty;
        var expectedSegments = new[] { JsonKeyToIdentifier(enumName) }
            .Concat(memberKey.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries).Select(JsonKeyToIdentifier))
            .ToArray();
        var info = infos.FirstOrDefault(item =>
            item.PathSegments.Count == expectedSegments.Length
            && item.PathSegments.Select(JsonKeyToIdentifier).SequenceEqual(expectedSegments, StringComparer.Ordinal));
        if (info is null)
        {
            return false;
        }

        expression = $"L.{GetLAccessPath(info)}";
        return true;
    }

    private static string GetLAccessPath(LStringInfo info) =>
        info.PathSegments.Count == 1
            ? info.KeyProperty
            : string.Join(".", info.PathSegments.Select(JsonKeyToIdentifier));

    private static IncrementalValuesProvider<((AdditionalText File, string TargetNamespace) Source, string FileName)> GetLocalizationFileProvider(
        IncrementalGeneratorInitializationContext context) =>
        context.AdditionalTextsProvider
            .Combine(
                context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName)
                    .Combine(context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
                    {
                        provider.GlobalOptions.TryGetValue(RootNamespaceProperty, out var rootNamespace);
                        return rootNamespace;
                    }))
                    .Select(static (values, _) => ResolveTargetNamespace(values.Left, values.Right)))
            .Combine(context.AnalyzerConfigOptionsProvider.Select(static (provider, _) => GetLocalizationFileName(provider)));

    private static string GetLocalizationFileName(AnalyzerConfigOptionsProvider provider)
    {
        if (provider.GlobalOptions.TryGetValue(LocalizationFileProperty, out var fileName) && !string.IsNullOrWhiteSpace(fileName))
        {
            return fileName;
        }

        return "l.json";
    }

    private static void CreateLResourceSource(SourceProductionContext context, string targetNamespace, IReadOnlyCollection<LStringInfo> infos)
    {
        var source = new StringBuilder();
        source.AppendLine("#nullable enable");
        source.AppendLine("using Senlinz.Localization;");
        source.AppendLine("using System.Collections.Generic;");
        AppendNamespaceStart(source, targetNamespace);
        source.AppendLine("    /// <summary>");
        source.AppendLine("    /// Base class for generated localization resources.");
        source.AppendLine("    /// </summary>");
        source.AppendLine("    public abstract class LResource : ILResource");
        source.AppendLine("    {");
        AppendSummary(source, "        ", "Gets the culture name.");
        source.AppendLine("        public abstract string Culture { get; }");

        foreach (var info in infos.Where(static item => item.PathSegments.Count == 1))
        {
            source.AppendLine();
            AppendSummary(source, "        ", info.DefaultValue);
            source.AppendLine($"        protected virtual string {info.KeyProperty} => {ToLiteral(info.DefaultValue)};");
        }

        source.AppendLine();
        AppendSummary(source, "        ", "Gets the resource dictionary.");
        source.AppendLine("        public virtual Dictionary<string, string> GetResource() => new()");
        source.AppendLine("        {");
        foreach (var info in infos.Where(static item => item.PathSegments.Count == 1))
        {
            source.AppendLine($"            {{ {ToLiteral(info.Key)}, {info.KeyProperty} }},");
        }

        source.AppendLine("        };");
        source.AppendLine("    }");
        AppendNamespaceEnd(source, targetNamespace);
        source.Append("#nullable restore");
        context.AddSource("LResource.g.cs", source.ToString());
    }

    private static void CreateLSource(SourceProductionContext context, string targetNamespace, IReadOnlyCollection<LStringInfo> infos)
    {
        var source = new StringBuilder();
        source.AppendLine("#nullable enable");
        source.AppendLine("using Senlinz.Localization;");
        AppendNamespaceStart(source, targetNamespace);
        AppendSummary(source, "    ", "Auto-generated localization keys.");
        source.AppendLine($"    [System.CodeDom.Compiler.GeneratedCodeAttribute(\"{ExecutingAssembly.Name}\", \"{ExecutingAssembly.Version}\")]");
        source.AppendLine("    public partial class L");
        source.AppendLine("    {");

        var first = true;
        foreach (var info in infos.Where(static item => item.PathSegments.Count == 1))
        {
            if (!first)
            {
                source.AppendLine();
            }

            first = false;
            AppendSummary(source, "        ", info.DefaultValue);
            if (info.Parameters.Count == 0)
            {
                source.AppendLine($"        public static LString {info.KeyProperty} = new({ToLiteral(info.Key)}, {ToLiteral(info.DefaultValue)});");
                continue;
            }

            source.Append($"        public static LString {info.KeyProperty}(");
            source.Append(string.Join(", ", info.Parameters.Select(parameter => $"string {parameter.ParameterName}")));
            source.AppendLine(")");
            source.AppendLine("        {");
            source.AppendLine("            return new LString(");
            source.AppendLine($"                {ToLiteral(info.Key)},");
            source.AppendLine($"                {ToLiteral(info.DefaultValue)},");
            source.AppendLine("                new[]");
            source.AppendLine("                {");
            foreach (var parameter in info.Parameters)
            {
                source.AppendLine($"                    new KeyValuePair<string, string>({ToLiteral(parameter.Token)}, {parameter.ParameterName}),");
            }

            source.AppendLine("                });");
            source.AppendLine("        }");
        }

        AppendNestedLApi(source, infos);

        source.AppendLine("    }");
        AppendNamespaceEnd(source, targetNamespace);
        source.Append("#nullable restore");
        context.AddSource("L.g.cs", source.ToString());
    }

    private static void CreateGlobalAliasesSource(SourceProductionContext context, string targetNamespace)
    {
        if (string.IsNullOrWhiteSpace(targetNamespace))
        {
            return;
        }

        var source = new StringBuilder();
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine($"global using L = {targetNamespace}.L;");
        source.AppendLine($"global using LResource = {targetNamespace}.LResource;");
        source.AppendLine($"global using IL = {targetNamespace}.IL;");
        source.Append("#nullable restore");
        context.AddSource("LAliases.g.cs", source.ToString());
    }

    private static void AppendNestedLApi(StringBuilder source, IReadOnlyCollection<LStringInfo> infos)
    {
        var root = BuildNestedLApiTree(infos);
        if (root.Children.Count == 0)
        {
            return;
        }

        source.AppendLine();
        AppendNestedLApiMembers(source, "        ", root);
    }

    private static NestedLApiNode BuildNestedLApiTree(IEnumerable<LStringInfo> infos)
    {
        var root = new NestedLApiNode(string.Empty, string.Empty);
        foreach (var info in infos.Where(static item => item.PathSegments.Count > 1))
        {
            var current = root;
            for (var index = 0; index < info.PathSegments.Count - 1; index++)
            {
                var identifier = JsonKeyToIdentifier(info.PathSegments[index]);
                current = current.GetOrAddChild(identifier);
            }

            current.Leaves.Add(new NestedLApiLeaf(JsonKeyToIdentifier(info.PathSegments[info.PathSegments.Count - 1]), info));
        }

        return root;
    }

    private static void AppendNestedLApiMembers(StringBuilder source, string indent, NestedLApiNode node)
    {
        foreach (var child in node.Children.Values)
        {
            source.AppendLine($"{indent}public {(node.IsRoot ? "static " : string.Empty)}{child.TypeName} {child.Identifier} {{ get; }} = new();");
        }

        foreach (var leaf in node.Leaves)
        {
            source.AppendLine();
            AppendSummary(source, indent, leaf.Info.DefaultValue);
            if (leaf.Info.Parameters.Count == 0)
            {
                source.AppendLine($"{indent}public LString {leaf.Identifier} => new({ToLiteral(leaf.Info.Key)}, {ToLiteral(leaf.Info.DefaultValue)});");
                continue;
            }

            source.Append($"{indent}public LString {leaf.Identifier}(");
            source.Append(string.Join(", ", leaf.Info.Parameters.Select(parameter => $"string {parameter.ParameterName}")));
            source.AppendLine(")");
            source.AppendLine($"{indent}{{");
            source.AppendLine($"{indent}    return new LString(");
            source.AppendLine($"{indent}        {ToLiteral(leaf.Info.Key)},");
            source.AppendLine($"{indent}        {ToLiteral(leaf.Info.DefaultValue)},");
            source.AppendLine($"{indent}        new[]");
            source.AppendLine($"{indent}        {{");
            foreach (var parameter in leaf.Info.Parameters)
            {
                source.AppendLine($"{indent}            new KeyValuePair<string, string>({ToLiteral(parameter.Token)}, {parameter.ParameterName}),");
            }

            source.AppendLine($"{indent}        }});");
            source.AppendLine($"{indent}}}");
        }

        foreach (var child in node.Children.Values)
        {
            source.AppendLine();
            source.AppendLine($"{indent}public sealed class {child.TypeName}");
            source.AppendLine($"{indent}{{");
            source.AppendLine($"{indent}    internal {child.TypeName}() {{ }}");
            AppendNestedLApiMembers(source, $"{indent}    ", child);
            source.AppendLine($"{indent}}}");
        }
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
        while (parent is not null && parent is not BaseNamespaceDeclarationSyntax)
        {
            parent = parent.Parent;
        }

        return parent is BaseNamespaceDeclarationSyntax namespaceSyntax
            ? namespaceSyntax.Name.ToString()
            : string.Empty;
    }

    private static string GetSeparator(EnumDeclarationSyntax enumSyntax)
    {
        foreach (var separatorArg in from attributeList in enumSyntax.AttributeLists
                 from attribute in attributeList.Attributes
                 where attribute.Name.ToString().StartsWith(LStringAttributePrefix, StringComparison.Ordinal)
                 select attribute.ArgumentList?.Arguments.FirstOrDefault())
        {
            if (separatorArg is not null)
            {
                return separatorArg.Expression.ToString().Trim('"');
            }
        }

        return "_";
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
            const string pattern = "(?<={)[^{}]+(?=})";
            var value = entry.Value;
            foreach (Match match in Regex.Matches(value, pattern))
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

    private static bool TryParseLocalizationEntries(string jsonText, out List<LocalizationEntry> entries)
    {
        entries = new List<LocalizationEntry>();
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(jsonText);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            FlattenLocalizationEntries(document.RootElement, new List<string>(), entries);
        }
        catch (JsonException)
        {
            entries = new List<LocalizationEntry>();
            return false;
        }

        return entries.Count > 0;
    }

    private static void FlattenLocalizationEntries(JsonElement element, List<string> pathSegments, List<LocalizationEntry> entries)
    {
        foreach (var property in element.EnumerateObject())
        {
            pathSegments.Add(property.Name);
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                FlattenLocalizationEntries(property.Value, pathSegments, entries);
                pathSegments.RemoveAt(pathSegments.Count - 1);
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.String)
            {
                entries.Add(new LocalizationEntry(
                    GetFlattenedLocalizationKey(pathSegments),
                    property.Value.GetString() ?? string.Empty,
                    pathSegments.ToArray()));
            }

            pathSegments.RemoveAt(pathSegments.Count - 1);
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

    private static string EnsureUniqueParameterName(IEnumerable<LStringParameter> existingParameters, string candidate)
    {
        var parameterName = string.IsNullOrWhiteSpace(candidate) ? "value" : candidate;
        var suffix = 1;
        while (existingParameters.Any(parameter => parameter.ParameterName == parameterName))
        {
            suffix++;
            parameterName = $"{candidate}{suffix}";
        }

        return parameterName;
    }

    private static string JsonKeyToIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "_";
        }

        var builder = new StringBuilder();
        foreach (var character in value)
        {
            if (builder.Length == 0)
            {
                if (character == '_' || char.IsLetter(character))
                {
                    builder.Append(char.ToUpperInvariant(character));
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

        return builder.Length == 0 ? "_" : builder.ToString();
    }

    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "value";
        }

        return char.ToLowerInvariant(value[0]) + value.Substring(1);
    }

    private static string ToLiteral(string value) =>
        $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t")}\"";

    private static string EscapeXml(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

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

        public Dictionary<string, NestedLApiNode> Children { get; } = new(StringComparer.Ordinal);

        public List<NestedLApiLeaf> Leaves { get; } = new();

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
