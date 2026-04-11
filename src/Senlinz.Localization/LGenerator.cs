using System.Reflection;
using System.Text;
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
    private static readonly string[] LocalizationFileProperties =
    [
        "build_property.Mo.Localization.File",
        "build_property.MoLocalizationFile"
    ];
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
            if (!TryParseLocalizationDictionary(jsonText, out var dictionary))
            {
                return;
            }

            var infos = GetLStringInfos(dictionary);
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
        var enumProviders = context.SyntaxProvider.ForAttributeWithMetadataName(
                LStringAttributeName,
                static (node, _) => node is EnumDeclarationSyntax,
                static (syntaxContext, _) => (Symbol: (INamedTypeSymbol)syntaxContext.TargetSymbol, Syntax: (EnumDeclarationSyntax)syntaxContext.TargetNode));

        context.RegisterSourceOutput(enumProviders, (sourceContext, enumInfo) =>
        {
            var enumSymbol = enumInfo.Symbol;
            var enumSyntax = enumInfo.Syntax;
            var enumFields = enumSyntax.Members;
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
                var generatedKey = $"{enumName}{separator}{enumField.Identifier.Text}";
                var enumKeyPrefix = $"{enumName}{separator}";
                var lKeyProperty = JsonKeyToIdentifier(generatedKey);
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
                            var resolvedKey = attributeValue.StartsWith(enumKeyPrefix, StringComparison.Ordinal)
                                ? attributeValue
                                : $"{enumKeyPrefix}{attributeValue}";
                            lKeyProperty = JsonKeyToIdentifier(resolvedKey);
                        }
                    }
                }

                source.AppendLine($"                {enumName}.{enumField.Identifier.Text} => L.{lKeyProperty},");
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
        foreach (var propertyName in LocalizationFileProperties)
        {
            if (provider.GlobalOptions.TryGetValue(propertyName, out var fileName) && !string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
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
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Gets the culture name.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public abstract string Culture { get; }");

        foreach (var info in infos)
        {
            source.AppendLine();
            source.AppendLine("        /// <summary>");
            source.AppendLine($"        /// {EscapeXml(info.DefaultValue)}");
            source.AppendLine("        /// </summary>");
            source.AppendLine($"        protected abstract string {info.KeyProperty} {{ get; }}");
        }

        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Gets the resource dictionary.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public Dictionary<string, string> GetResource() => new()");
        source.AppendLine("        {");
        foreach (var info in infos)
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
        source.AppendLine("    /// <summary>");
        source.AppendLine("    /// Auto-generated localization keys.");
        source.AppendLine("    /// </summary>");
        source.AppendLine($"    [System.CodeDom.Compiler.GeneratedCodeAttribute(\"{ExecutingAssembly.Name}\", \"{ExecutingAssembly.Version}\")]");
        source.AppendLine("    public partial class L");
        source.AppendLine("    {");

        var first = true;
        foreach (var info in infos)
        {
            if (!first)
            {
                source.AppendLine();
            }

            first = false;
            source.AppendLine("        /// <summary>");
            source.AppendLine($"        /// {EscapeXml(info.DefaultValue)}");
            source.AppendLine("        /// </summary>");
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

    private static List<LStringInfo> GetLStringInfos(Dictionary<string, string> dictionary)
    {
        var result = new List<LStringInfo>();
        foreach (var pair in dictionary)
        {
            if (pair.Key is null || pair.Value is null)
            {
                continue;
            }

            var parameters = new List<LStringParameter>();
            const string pattern = "(?<={)[^{}]+(?=})";
            var value = pair.Value;
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

            result.Add(new LStringInfo(pair.Key, value, JsonKeyToIdentifier(pair.Key), parameters));
        }

        return result;
    }

    private static bool TryParseLocalizationDictionary(string jsonText, out Dictionary<string, string> dictionary)
    {
        dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return false;
        }

        foreach (Match match in Regex.Matches(
                     jsonText,
                     "\"(?<key>(?:\\\\.|[^\"\\\\])*)\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\""))
        {
            dictionary[UnescapeJson(match.Groups["key"].Value)] = UnescapeJson(match.Groups["value"].Value);
        }

        return dictionary.Count > 0;
    }

    private static string UnescapeJson(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character != '\\' || index == value.Length - 1)
            {
                builder.Append(character);
                continue;
            }

            index++;
            switch (value[index])
            {
                case '"':
                    builder.Append('"');
                    break;
                case '\\':
                    builder.Append('\\');
                    break;
                case '/':
                    builder.Append('/');
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'u' when index + 4 < value.Length:
                    builder.Append((char)Convert.ToInt32(value.Substring(index + 1, 4), 16));
                    index += 4;
                    break;
                default:
                    builder.Append(value[index]);
                    break;
            }
        }

        return builder.ToString();
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
        var capitalizeNext = true;
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                capitalizeNext = true;
                continue;
            }

            if (builder.Length == 0 && char.IsDigit(character))
            {
                builder.Append('_');
            }

            builder.Append(capitalizeNext ? char.ToUpperInvariant(character) : character);
            capitalizeNext = false;
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

    private sealed class LStringInfo
    {
        public LStringInfo(string key, string defaultValue, string keyProperty, IReadOnlyList<LStringParameter> parameters)
        {
            Key = key;
            DefaultValue = defaultValue;
            KeyProperty = keyProperty;
            Parameters = parameters;
        }

        public string Key { get; }

        public string DefaultValue { get; }

        public string KeyProperty { get; }

        public IReadOnlyList<LStringParameter> Parameters { get; }
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
