using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Senlinz.Localization;

public sealed partial class LGenerator
{
    private static void AddEnumAttributeSource(IncrementalGeneratorInitializationContext context)
    {
        var localizationInfosProvider = GetLocalizationStateProvider(context)
            .Select(static (state, _) => state.PrimaryFile?.Infos ?? ImmutableArray<LStringInfo>.Empty);

        var enumProviders = context.SyntaxProvider.ForAttributeWithMetadataName(
                LStringAttributeName,
                static (node, _) => node is EnumDeclarationSyntax,
                static (syntaxContext, _) => (Symbol: (INamedTypeSymbol)syntaxContext.TargetSymbol, Syntax: (EnumDeclarationSyntax)syntaxContext.TargetNode));

        context.RegisterSourceOutput(enumProviders.Combine(localizationInfosProvider), (sourceContext, pair) =>
        {
            var enumInfo = pair.Left;
            var enumSymbol = enumInfo.Symbol;
            var enumSyntax = enumInfo.Syntax;
            var enumFields = enumSyntax.Members;
            var infos = pair.Right;
            if (enumFields.Count == 0)
            {
                return;
            }

            var enumName = enumSymbol.Name;
            var enumNamespace = GetNamespace(enumSyntax);
            var enumParameterName = ToCamelCase(enumName);
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
                var lAccessExpression = ResolveEnumLAccessExpression(enumName, enumField, infos);
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
        EnumMemberDeclarationSyntax enumField,
        IReadOnlyCollection<LStringInfo> infos)
    {
        var enumSegment = ToCamelCase(JsonKeyToIdentifier(enumName));
        var memberSegment = ToCamelCase(JsonKeyToIdentifier(enumField.Identifier.Text));
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
                    memberSegment = NormalizeEnumMemberSegment(attributeValue, enumName, enumSegment);
                }
            }
        }

        var generatedKey = $"{enumSegment}.{memberSegment}";
        if (TryGetLAccessExpressionForKey(infos, generatedKey, out var expression))
        {
            return expression;
        }

        return $"L.{JsonKeyToIdentifier(enumSegment)}.{JsonKeyToIdentifier(memberSegment)}";
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

    private static string GetLAccessPath(LStringInfo info) =>
        info.PathSegments.Count == 1
            ? info.KeyProperty
            : string.Join(".", info.PathSegments.Select(JsonKeyToIdentifier));

    private static string NormalizeEnumMemberSegment(string value, string enumName, string enumSegment)
    {
        const int UnderscoreLength = 1;
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var dottedSegments = trimmed.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        var candidate = dottedSegments[dottedSegments.Length - 1];
        if (candidate.StartsWith($"{enumName}_", StringComparison.Ordinal))
        {
            candidate = candidate.Substring(enumName.Length + UnderscoreLength);
        }
        else if (candidate.StartsWith($"{enumSegment}_", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate.Substring(enumSegment.Length + UnderscoreLength);
        }

        return ToCamelCase(JsonKeyToIdentifier(candidate));
    }
}
