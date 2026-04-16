using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Senlinz.Localization;

public sealed partial class LGenerator
{
    private static void CreateLSource(SourceProductionContext context, string targetNamespace, IReadOnlyCollection<LStringInfo> infos)
    {
        var source = new StringBuilder();
        source.AppendLine("#nullable enable");
        source.AppendLine("using System.Collections.Generic;");
        source.AppendLine("using Senlinz.Localization;");
        AppendNamespaceStart(source, targetNamespace);
        source.AppendLine("    /// <summary>");
        source.AppendLine("    /// Generated localization accessors.");
        source.AppendLine("    /// </summary>");
        source.AppendLine($"    [System.CodeDom.Compiler.GeneratedCodeAttribute(\"{ExecutingAssembly.Name}\", \"{ExecutingAssembly.Version}\")]");
        source.AppendLine("    public static partial class L");
        source.AppendLine("    {");

        var first = true;
        foreach (var info in infos)
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
}
