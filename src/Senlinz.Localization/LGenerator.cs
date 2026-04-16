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

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        AddJsonLocalizationSource(context);
        AddEnumAttributeSource(context);
    }

    private static void AddJsonLocalizationSource(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(GetLocalizationStateProvider(context), (sourceContext, state) =>
        {
            if (state.PrimaryFile is null)
            {
                return;
            }

            var targetNamespace = state.TargetNamespace;
            var primaryFile = state.PrimaryFile;
            var infos = primaryFile.Infos;
            CreateLSource(sourceContext, targetNamespace, infos);
            CreateLResourceBaseSource(sourceContext, targetNamespace, infos);
            foreach (var file in state.Files)
            {
                CreateResourceSource(sourceContext, targetNamespace, primaryFile, file);
            }

            CreateLStringResolverSource(sourceContext, targetNamespace, state.Files);
        });
    }

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

    private static IncrementalValueProvider<LocalizationGenerationState> GetLocalizationStateProvider(IncrementalGeneratorInitializationContext context) =>
        GetLocalizationFilesProvider(context)
            .Collect()
            .Combine(GetTargetNamespaceProvider(context))
            .Combine(context.AnalyzerConfigOptionsProvider.Select(static (provider, _) => GetLocalizationFileName(provider)))
            .Select(static (values, _) => CreateLocalizationGenerationState(values.Left.Left, values.Left.Right, values.Right));

    private static IncrementalValuesProvider<LocalizationFileModel> GetLocalizationFilesProvider(IncrementalGeneratorInitializationContext context) =>
        context.AdditionalTextsProvider
            .Where(static file => IsLocalizationJsonFile(file.Path))
            .Select(static (file, _) => ParseLocalizationFile(file))
            .Where(static file => file is not null)
            .Select(static (file, _) => file!);

    private static IncrementalValueProvider<string> GetTargetNamespaceProvider(IncrementalGeneratorInitializationContext context) =>
        context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName)
            .Combine(context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue(RootNamespaceProperty, out var rootNamespace);
                return rootNamespace;
            }))
            .Select(static (values, _) => ResolveTargetNamespace(values.Left, values.Right));

    private static LocalizationGenerationState CreateLocalizationGenerationState(
        ImmutableArray<LocalizationFileModel> files,
        string targetNamespace,
        string primaryFileName)
    {
        var orderedFiles = files
            .OrderBy(static file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
        var primaryFile = orderedFiles.FirstOrDefault(file => string.Equals(file.FileName, primaryFileName, StringComparison.OrdinalIgnoreCase));
        return new LocalizationGenerationState(targetNamespace, orderedFiles, primaryFile);
    }

    private static bool IsLocalizationJsonFile(string path) =>
        string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);

    private static LocalizationFileModel? ParseLocalizationFile(AdditionalText file)
    {
        var fileName = Path.GetFileName(file.Path);
        var cultureName = Path.GetFileNameWithoutExtension(file.Path);
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(cultureName))
        {
            return null;
        }

        var jsonText = file.GetText()?.ToString() ?? string.Empty;
        if (!TryParseLocalizationEntries(jsonText, out var entries))
        {
            return null;
        }

        return new LocalizationFileModel(
            file.Path,
            fileName,
            cultureName,
            GetResourceClassName(cultureName),
            GetLStringInfos(entries).ToImmutableArray());
    }

    private static string GetLocalizationFileName(AnalyzerConfigOptionsProvider provider)
    {
        if (provider.GlobalOptions.TryGetValue(LocalizationFileProperty, out var fileName) && !string.IsNullOrWhiteSpace(fileName))
        {
            return fileName;
        }

        return "en.json";
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

            builder.Append(char.ToUpperInvariant(identifier[0]));
            if (identifier.Length > 1)
            {
                builder.Append(identifier.Substring(1));
            }
        }

        if (builder.Length == 0)
        {
            builder.Append("Localization");
        }

        return $"{builder}Resource";
    }

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

        foreach (var info in infos.Where(static item => item.PathSegments.Count == 1))
        {
            source.AppendLine();
            AppendSummary(source, "        ", info.DefaultValue);
            source.AppendLine($"        protected abstract string {info.KeyProperty} {{ get; }}");
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

    private static void CreateResourceSource(
        SourceProductionContext context,
        string targetNamespace,
        LocalizationFileModel primaryFile,
        LocalizationFileModel file)
    {
        var source = new StringBuilder();
        var values = file.Infos.ToDictionary(static info => info.Key, static info => info.DefaultValue, StringComparer.Ordinal);
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

        foreach (var info in primaryFile.Infos.Where(static item => item.PathSegments.Count == 1))
        {
            source.AppendLine();
            values.TryGetValue(info.Key, out var localizedValue);
            AppendSummary(source, "        ", localizedValue ?? string.Empty);
            source.AppendLine($"        protected override string {info.KeyProperty} => {ToLiteral(localizedValue ?? string.Empty)};");
        }

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
        source.AppendLine("        private readonly ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<string, string>>> _dictionaries = new();");
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
        source.AppendLine("        public static LStringResolver Create(GetCulture getCulture) => new(getCulture);");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Creates a resolver that uses the provided resources directly.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public static LStringResolver Create(GetCulture getCulture, params ILResource[] resources) => new(getCulture, resources);");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Creates a resolver that uses generated resources from the provided assembly.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public static LStringResolver Create(GetCulture getCulture, Assembly assembly) => new(getCulture, assembly);");
        source.AppendLine();
        source.AppendLine("        internal static GetCultureResource CreateCultureResource(params ILResource[] resources)");
        source.AppendLine("        {");
        source.AppendLine("            if (resources is null)");
        source.AppendLine("            {");
        source.AppendLine("                throw new ArgumentNullException(nameof(resources));");
        source.AppendLine("            }");
        source.AppendLine();
        source.AppendLine("            var resourceMap = new Dictionary<string, ILResource>(StringComparer.Ordinal);");
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
        source.AppendLine("                .Where(static type =>");
        source.AppendLine("                    type is { IsAbstract: false, IsClass: true } &&");
        source.AppendLine("                    typeof(IGeneratedLResource).IsAssignableFrom(type) &&");
        source.AppendLine("                    typeof(ILResource).IsAssignableFrom(type))");
        source.AppendLine("                .OrderBy(static type => type.FullName, StringComparer.Ordinal)");
        source.AppendLine("                .Select(static type => Activator.CreateInstance(type, nonPublic: true) as ILResource");
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
        source.AppendLine("        /// Creates a resolver that uses all generated resources from this project.");
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
        source.AppendLine("        public static new LStringResolver<T> Create(GetCulture getCulture) => new(getCulture);");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Creates a resolver that uses the provided resources directly.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public static new LStringResolver<T> Create(GetCulture getCulture, params ILResource[] resources) => new(getCulture, resources);");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Creates a resolver that uses generated resources from the provided assembly.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public static new LStringResolver<T> Create(GetCulture getCulture, Assembly assembly) => new(getCulture, assembly);");
        source.AppendLine("    }");
        AppendNamespaceEnd(source, targetNamespace);
        source.Append("#nullable restore");
        context.AddSource("LStringResolver.g.cs", source.ToString());
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
        public LocalizationGenerationState(string targetNamespace, ImmutableArray<LocalizationFileModel> files, LocalizationFileModel? primaryFile)
        {
            TargetNamespace = targetNamespace;
            Files = files;
            PrimaryFile = primaryFile;
        }

        public string TargetNamespace { get; }

        public ImmutableArray<LocalizationFileModel> Files { get; }

        public LocalizationFileModel? PrimaryFile { get; }
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
