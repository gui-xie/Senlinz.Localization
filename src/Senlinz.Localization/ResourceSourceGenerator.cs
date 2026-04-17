using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Senlinz.Localization
{

public sealed partial class LGenerator
{
    private static void CreateLResourceBaseSource(SourceProductionContext context, string targetNamespace, IReadOnlyCollection<LStringInfo> infos)
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

        source.AppendLine();
        AppendSummary(source, "        ", "Gets the default resource dictionary from the primary localization file, including nested dotted keys.");
        source.AppendLine("        public virtual Dictionary<string, string> GetResource()");
        source.AppendLine("        {");
        source.AppendLine("            return new Dictionary<string, string>");
        source.AppendLine("            {");
        foreach (var info in infos)
        {
            source.AppendLine($"            {{ {ToLiteral(info.Key)}, {ToLiteral(info.DefaultValue)} }},");
        }

        source.AppendLine("        };");
        source.AppendLine("        }");
        source.AppendLine("    }");
        AppendNamespaceEnd(source, targetNamespace);
        source.Append("#nullable restore");
        context.AddSource("LResource.g.cs", source.ToString());
    }

    private static void CreateResourceSource(
        SourceProductionContext context,
        string targetNamespace,
        LocalizationFileModel primaryFile,
        LocalizationFileModel file)
    {
        var source = new StringBuilder();
        source.AppendLine("#nullable enable");
        source.AppendLine("using Senlinz.Localization;");
        source.AppendLine("using System.Collections.Generic;");
        AppendNamespaceStart(source, targetNamespace);
        AppendSummary(source, "    ", $"Generated localization resource for culture '{file.CultureName}'.");
        source.AppendLine($"    [System.CodeDom.Compiler.GeneratedCodeAttribute(\"{ExecutingAssembly.Name}\", \"{ExecutingAssembly.Version}\")]");
        var implementedInterfaces = new List<string> { "IGeneratedLResource" };
        if (string.Equals(primaryFile.FileName, file.FileName, StringComparison.OrdinalIgnoreCase))
        {
            implementedInterfaces.Add("IDefaultLResource");
        }

        source.AppendLine($"    internal sealed class {file.ResourceClassName} : LResource, {string.Join(", ", implementedInterfaces)}");
        source.AppendLine("    {");
        AppendSummary(source, "        ", "Gets the culture name.");
        source.AppendLine($"        public override string Culture => {ToLiteral(file.CultureName)};");

        if (!string.Equals(primaryFile.FileName, file.FileName, StringComparison.OrdinalIgnoreCase))
        {
            source.AppendLine();
            AppendSummary(source, "        ", "Gets the generated resource dictionary.");
            source.AppendLine("        public override Dictionary<string, string> GetResource()");
            source.AppendLine("        {");
            source.AppendLine("            var resource = base.GetResource();");
            foreach (var info in file.Infos)
            {
                source.AppendLine($"            resource[{ToLiteral(info.Key)}] = {ToLiteral(info.DefaultValue)};");
            }

            source.AppendLine("            return resource;");
            source.AppendLine("        }");
        }
        else
        {
            source.AppendLine();
            AppendSummary(source, "        ", "Uses the primary localization dictionary inherited from the generated base resource.");
        }

        source.AppendLine("    }");
        AppendNamespaceEnd(source, targetNamespace);
        source.Append("#nullable restore");
        context.AddSource($"{file.ResourceClassName}.g.cs", source.ToString());
    }

    private static void CreateLStringResolverSource(
        SourceProductionContext context,
        string targetNamespace,
        IReadOnlyCollection<LocalizationFileModel> files)
    {
        var source = new StringBuilder();
        source.AppendLine("#nullable enable");
        source.AppendLine("using System;");
        source.AppendLine("using System.Collections.Concurrent;");
        source.AppendLine("using System.Collections.Generic;");
        source.AppendLine("using System.Linq;");
        source.AppendLine("using System.Reflection;");
        source.AppendLine("using Senlinz.Localization;");
        AppendNamespaceStart(source, targetNamespace);
        source.AppendLine("    /// <summary>");
        source.AppendLine("    /// Resolves <see cref=\"LString\"/> values for the active culture.");
        source.AppendLine("    /// </summary>");
        source.AppendLine($"    [System.CodeDom.Compiler.GeneratedCodeAttribute(\"{ExecutingAssembly.Name}\", \"{ExecutingAssembly.Version}\")]");
        source.AppendLine("    public class LStringResolver : ILStringResolver");
        source.AppendLine("    {");
        source.AppendLine("        private static readonly IReadOnlyDictionary<string, string> EmptyDictionary = new Dictionary<string, string>();");
        source.AppendLine("        private static readonly ILResource[] GeneratedResources = CreateGeneratedResources();");
        source.AppendLine();
        source.AppendLine("        private readonly GetCulture _getCulture;");
        source.AppendLine("        private readonly GetCultureResource _getCultureResource;");
        source.AppendLine("        private readonly ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<string, string>>> _dictionaries = new ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<string, string>>>();");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Creates a resolver that uses all generated resources from this project.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public LStringResolver(GetCulture getCulture)");
        source.AppendLine("            : this(getCulture, CreateCultureResource(GeneratedResources))");
        source.AppendLine("        {");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Creates a resolver that uses the provided resources directly.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public LStringResolver(GetCulture getCulture, params ILResource[] resources)");
        source.AppendLine("            : this(getCulture, CreateCultureResource(resources))");
        source.AppendLine("        {");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Creates a resolver that uses generated resources from the provided assembly.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public LStringResolver(GetCulture getCulture, Assembly assembly)");
        source.AppendLine("            : this(getCulture, CreateCultureResource(CreateAll(assembly)))");
        source.AppendLine("        {");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Creates a resolver with a custom culture resource accessor.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        protected LStringResolver(GetCulture getCulture, GetCultureResource getCultureResource)");
        source.AppendLine("        {");
        source.AppendLine("            _getCulture = getCulture ?? throw new ArgumentNullException(nameof(getCulture));");
        source.AppendLine("            _getCultureResource = getCultureResource ?? throw new ArgumentNullException(nameof(getCultureResource));");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Resolves a localizable string.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public string this[LString lString] => lString.Resolve(ResolveCore);");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Resolves a localizable string.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public string Resolve(LString lString) => this[lString];");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Creates a resolver that uses all generated resources from this project.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public static LStringResolver Create(GetCulture getCulture) => new LStringResolver(getCulture);");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Creates a resolver that uses the provided resources directly.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public static LStringResolver Create(GetCulture getCulture, params ILResource[] resources) => new LStringResolver(getCulture, resources);");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Creates a resolver that uses generated resources from the provided assembly.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public static LStringResolver Create(GetCulture getCulture, Assembly assembly) => new LStringResolver(getCulture, assembly);");
        source.AppendLine();
        source.AppendLine("        internal static GetCultureResource CreateCultureResource(params ILResource[] resources)");
        source.AppendLine("        {");
        source.AppendLine("            if (resources is null)");
        source.AppendLine("            {");
        source.AppendLine("                throw new ArgumentNullException(nameof(resources));");
        source.AppendLine("            }");
        source.AppendLine();
        source.AppendLine("            var resourceMap = new Dictionary<string, ILResource>(resources.Length, StringComparer.OrdinalIgnoreCase);");
        source.AppendLine("            for (var index = 0; index < resources.Length; index++)");
        source.AppendLine("            {");
        source.AppendLine("                var resource = resources[index] ?? throw new ArgumentNullException(nameof(resources), $\"Resource at index {index} is null.\");");
        source.AppendLine("                if (resourceMap.ContainsKey(resource.Culture))");
        source.AppendLine("                {");
        source.AppendLine("                    throw new ArgumentException($\"Duplicate resource culture '{resource.Culture}' was provided.\", nameof(resources));");
        source.AppendLine("                }");
        source.AppendLine();
        source.AppendLine("                resourceMap.Add(resource.Culture, resource);");
        source.AppendLine("            }");
        source.AppendLine();
        source.AppendLine("            return culture =>");
        source.AppendLine("            {");
        source.AppendLine("                resourceMap.TryGetValue(culture, out var resource);");
        source.AppendLine("                return resource?.GetResource() ?? new Dictionary<string, string>();");
        source.AppendLine("            };");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        private string ResolveCore(string key)");
        source.AppendLine("        {");
        source.AppendLine("            var culture = _getCulture();");
        source.AppendLine("            var dictionary = _dictionaries.GetOrAdd(");
        source.AppendLine("                culture,");
        source.AppendLine("                _ => new Lazy<IReadOnlyDictionary<string, string>>(() => _getCultureResource(culture) ?? EmptyDictionary));");
        source.AppendLine();
        source.AppendLine("            if (dictionary.Value.TryGetValue(key, out var value))");
        source.AppendLine("            {");
        source.AppendLine("                return value;");
        source.AppendLine("            }");
        source.AppendLine();
        source.AppendLine("            return string.Empty;");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        private static ILResource[] CreateGeneratedResources() =>");
        source.AppendLine("            new ILResource[]");
        source.AppendLine("            {");
        foreach (var file in files)
        {
            source.AppendLine($"                new {file.ResourceClassName}(),");
        }

        source.AppendLine("            };");
        source.AppendLine();
        source.AppendLine("        private static ILResource[] CreateAll(Assembly assembly)");
        source.AppendLine("        {");
        source.AppendLine("            if (assembly is null)");
        source.AppendLine("            {");
        source.AppendLine("                throw new ArgumentNullException(nameof(assembly));");
        source.AppendLine("            }");
        source.AppendLine();
        source.AppendLine("            var candidates = assembly");
        source.AppendLine("                .GetTypes()");
        source.AppendLine("                .Where(type =>");
        source.AppendLine("                    type is { IsAbstract: false, IsClass: true } &&");
        source.AppendLine("                    typeof(IGeneratedLResource).IsAssignableFrom(type) &&");
        source.AppendLine("                    typeof(ILResource).IsAssignableFrom(type))");
        source.AppendLine("                .OrderBy(type => type.FullName, StringComparer.Ordinal)");
        source.AppendLine("                .Select(type => Activator.CreateInstance(type, nonPublic: true) as ILResource");
        source.AppendLine("                    ?? throw new InvalidOperationException($\"Unable to create generated localization resource '{type.FullName}'.\"))");
        source.AppendLine("                .ToArray();");
        source.AppendLine();
        source.AppendLine("            if (candidates.Length == 0)");
        source.AppendLine("            {");
        source.AppendLine("                throw new InvalidOperationException($\"No generated localization resources were found in assembly '{assembly.FullName}'.\");");
        source.AppendLine("            }");
        source.AppendLine();
        source.AppendLine("            return candidates;");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    /// <summary>");
        source.AppendLine("    /// Resolves <see cref=\"LString\"/> values for this project's generated resources.");
        source.AppendLine("    /// </summary>");
        source.AppendLine($"    [System.CodeDom.Compiler.GeneratedCodeAttribute(\"{ExecutingAssembly.Name}\", \"{ExecutingAssembly.Version}\")]");
        source.AppendLine("    public sealed class LStringResolver<T> : LStringResolver, ILStringResolver<T>");
        source.AppendLine("    {");
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Creates a marker-typed resolver that uses all generated resources from this project.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public LStringResolver(GetCulture getCulture)");
        source.AppendLine("            : base(getCulture)");
        source.AppendLine("        {");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Creates a resolver that uses the provided resources directly.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public LStringResolver(GetCulture getCulture, params ILResource[] resources)");
        source.AppendLine("            : base(getCulture, resources)");
        source.AppendLine("        {");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Creates a resolver that uses generated resources from the provided assembly.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public LStringResolver(GetCulture getCulture, Assembly assembly)");
        source.AppendLine("            : base(getCulture, assembly)");
        source.AppendLine("        {");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Creates a resolver with a custom culture resource accessor.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        private LStringResolver(GetCulture getCulture, GetCultureResource getCultureResource)");
        source.AppendLine("            : base(getCulture, getCultureResource)");
        source.AppendLine("        {");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Creates a resolver that uses all generated resources from this project.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public static new LStringResolver<T> Create(GetCulture getCulture) => new LStringResolver<T>(getCulture);");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Creates a resolver that uses the provided resources directly.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public static new LStringResolver<T> Create(GetCulture getCulture, params ILResource[] resources) => new LStringResolver<T>(getCulture, resources);");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Creates a resolver that uses generated resources from the provided assembly.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public static new LStringResolver<T> Create(GetCulture getCulture, Assembly assembly) => new LStringResolver<T>(getCulture, assembly);");
        source.AppendLine("    }");
        AppendNamespaceEnd(source, targetNamespace);
        source.Append("#nullable restore");
        context.AddSource("LStringResolver.g.cs", source.ToString());
    }
}
}
