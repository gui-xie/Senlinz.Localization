using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json;

namespace Senlinz.Localization;

/// <summary>
/// Generates localization helpers from JSON files and enum attributes.
/// </summary>
[Generator]
public sealed class LGenerator : IIncrementalGenerator
{
    private static readonly AssemblyName ExecutingAssembly = Assembly.GetExecutingAssembly().GetName();
    private const string LocalizationFileProperty = "build_property.MoLocalizationFile";
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
            var file = pair.Left.Left;
            var assemblyName = pair.Left.Right;
            var configuredFileName = pair.Right;

            if (assemblyName is null || !file.Path.EndsWith(configuredFileName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var jsonText = file.GetText()?.ToString() ?? string.Empty;
            var dictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonText);
            if (dictionary is null)
            {
                return;
            }

            var infos = GetLStringInfos(dictionary);
            CreateLSource(sourceContext, assemblyName, infos);
            CreateLResourceSource(sourceContext, assemblyName, infos);
            CreateDynamicResolverInterface(sourceContext, assemblyName);
        });
    }

    private static void CreateDynamicResolverInterface(SourceProductionContext context, string assemblyName)
    {
        var source = new StringBuilder();
        source.AppendLine("#nullable enable");
        source.AppendLine("using Senlinz.Localization;");
        source.AppendLine($"namespace {assemblyName}");
        source.AppendLine("{");
        source.AppendLine("    /// <summary>");
        source.AppendLine("    /// Typed localization resolver contract.");
        source.AppendLine("    /// </summary>");
        source.AppendLine("    public interface IL : ILStringResolver");
        source.AppendLine("    {");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.Append("#nullable restore");
        context.AddSource("IL.g.cs", source.ToString());
    }

    private static void AddEnumAttributeSource(IncrementalGeneratorInitializationContext context)
    {
        var assemblyNameProvider = context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName);

        var enumProviders = context.SyntaxProvider.ForAttributeWithMetadataName(
                LStringAttributeName,
                static (node, _) => node is EnumDeclarationSyntax,
                static (syntaxContext, _) => (Symbol: (INamedTypeSymbol)syntaxContext.TargetSymbol, Syntax: (EnumDeclarationSyntax)syntaxContext.TargetNode))
            .Combine(assemblyNameProvider);

        context.RegisterSourceOutput(enumProviders, (sourceContext, info) =>
        {
            var (enumInfo, assemblyName) = info;
            if (assemblyName is null)
            {
                return;
            }

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
            if (!string.IsNullOrWhiteSpace(enumNamespace) && enumNamespace != assemblyName)
            {
                source.AppendLine($"using {enumNamespace};");
            }

            source.AppendLine();
            source.AppendLine($"namespace {assemblyName}");
            source.AppendLine("{");
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
                var lKeyProperty = $"{enumName}{separator}{enumField.Identifier.Text}";
                foreach (var attributeList in enumField.AttributeLists)
                {
                    foreach (var attribute in attributeList.Attributes)
                    {
                        if (!attribute.Name.ToString().EndsWith(LStringKeyAttributeSuffix, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var attributeValue = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression.ToString().Trim('"');
                        if (!string.IsNullOrWhiteSpace(attributeValue))
                        {
                            lKeyProperty = JsonKeyToIdentifier(attributeValue);
                        }
                    }
                }

                source.AppendLine($"                {enumName}.{enumField.Identifier.Text} => L.{lKeyProperty},");
            }

            source.AppendLine("                _ => LString.Empty");
            source.AppendLine("            };");
            source.AppendLine("        }");
            source.AppendLine("    }");
            source.AppendLine("}");
            source.Append("#nullable restore");
            sourceContext.AddSource($"{className}.g.cs", source.ToString());
        });
    }

    private static IncrementalValuesProvider<((AdditionalText Left, string? Right) Left, string Right)> GetLocalizationFileProvider(
        IncrementalGeneratorInitializationContext context) =>
        context.AdditionalTextsProvider
            .Combine(context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName))
            .Combine(context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue(LocalizationFileProperty, out var fileName);
                return string.IsNullOrWhiteSpace(fileName) ? "l.json" : fileName;
            }));

    private static void CreateLResourceSource(SourceProductionContext context, string assemblyName, IReadOnlyCollection<LStringInfo> infos)
    {
        var source = new StringBuilder();
        source.AppendLine("#nullable enable");
        source.AppendLine("using Senlinz.Localization;");
        source.AppendLine("using System.Collections.Generic;");
        source.AppendLine($"namespace {assemblyName}");
        source.AppendLine("{");
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
        source.AppendLine("}");
        source.Append("#nullable restore");
        context.AddSource("LResource.g.cs", source.ToString());
    }

    private static void CreateLSource(SourceProductionContext context, string assemblyName, IReadOnlyCollection<LStringInfo> infos)
    {
        var source = new StringBuilder();
        source.AppendLine("#nullable enable");
        source.AppendLine("using Senlinz.Localization;");
        source.AppendLine($"namespace {assemblyName}");
        source.AppendLine("{");
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
        source.AppendLine("}");
        source.Append("#nullable restore");
        context.AddSource("L.g.cs", source.ToString());
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
                if (match.Value.StartsWith('$'))
                {
                    value = value.Replace(match.Value, match.Value.Substring(1), StringComparison.Ordinal);
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

    private static string ToLiteral(string value) => SymbolDisplay.FormatLiteral(value, true);

    private static string EscapeXml(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private sealed record LStringInfo(string Key, string DefaultValue, string KeyProperty, IReadOnlyList<LStringParameter> Parameters);

    private sealed record LStringParameter(string Token, string ParameterName);
}
