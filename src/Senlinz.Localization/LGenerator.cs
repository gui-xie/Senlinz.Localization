using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Senlinz.Localization
{

/// <summary>
/// Generates localization helpers from JSON files and enum attributes.
/// </summary>
[Generator]
public sealed partial class LGenerator : IIncrementalGenerator
{
    private static readonly AssemblyName ExecutingAssembly = Assembly.GetExecutingAssembly().GetName();
    private static readonly DiagnosticDescriptor InvalidLocalizationJsonDescriptor = new DiagnosticDescriptor(
        id: "SL001",
        title: "Invalid localization JSON",
        messageFormat: "Localization file '{0}' could not be parsed: {1}",
        category: "Senlinz.Localization",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor DuplicateLocalizationKeyDescriptor = new DiagnosticDescriptor(
        id: "SL002",
        title: "Duplicate localization key",
        messageFormat: "Localization file '{0}' contains duplicate localization key '{1}'",
        category: "Senlinz.Localization",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor ConflictingLocalizationIdentifierDescriptor = new DiagnosticDescriptor(
        id: "SL003",
        title: "Conflicting localization identifier",
        messageFormat: "Localization file '{0}' generates conflicting identifier '{1}': {2}",
        category: "Senlinz.Localization",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor PrimaryLocalizationFileNotFoundDescriptor = new DiagnosticDescriptor(
        id: "SL004",
        title: "Primary localization file not found",
        messageFormat: "Primary localization file '{0}' was not found among AdditionalFiles",
        category: "Senlinz.Localization",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
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
            foreach (var diagnostic in state.Diagnostics)
            {
                sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic());
            }

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

    private static IncrementalValueProvider<LocalizationGenerationState> GetLocalizationStateProvider(IncrementalGeneratorInitializationContext context) =>
        GetLocalizationFilesProvider(context)
            .Collect()
            .Combine(GetTargetNamespaceProvider(context))
            .Combine(context.AnalyzerConfigOptionsProvider.Select((provider, _) => GetLocalizationFileName(provider)))
            .Select((values, _) => CreateLocalizationGenerationState(values.Left.Left, values.Left.Right, values.Right));

    private static IncrementalValuesProvider<LocalizationFileCandidate> GetLocalizationFilesProvider(IncrementalGeneratorInitializationContext context) =>
        context.AdditionalTextsProvider
            .Where(file => IsLocalizationJsonFile(file.Path))
            .Select((file, _) => CreateLocalizationFileCandidate(file))
            .Where(file => file != null)
            .Select((file, _) => file!);

    private static IncrementalValueProvider<string> GetTargetNamespaceProvider(IncrementalGeneratorInitializationContext context) =>
        context.CompilationProvider.Select((compilation, _) => compilation.AssemblyName)
            .Combine(context.AnalyzerConfigOptionsProvider.Select((provider, _) =>
            {
                provider.GlobalOptions.TryGetValue(RootNamespaceProperty, out var rootNamespace);
                return rootNamespace;
            }))
            .Select((values, _) => ResolveTargetNamespace(values.Left, values.Right));

    private static LocalizationGenerationState CreateLocalizationGenerationState(
        ImmutableArray<LocalizationFileCandidate> files,
        string targetNamespace,
        string primaryFileName)
    {
        var diagnostics = ImmutableArray.CreateBuilder<LocalizationDiagnosticInfo>();
        var orderedFiles = files
            .Select(candidate => ParseLocalizationFile(candidate))
            .SelectMany(result =>
            {
                diagnostics.AddRange(result.Diagnostics);
                return result.File is null ? Enumerable.Empty<LocalizationFileModel>() : Enumerable.Repeat(result.File, 1);
            })
            .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
        var primaryFile = orderedFiles.FirstOrDefault(file => string.Equals(file.FileName, primaryFileName, StringComparison.OrdinalIgnoreCase));
        if (primaryFile is null)
        {
            diagnostics.Add(CreateProjectDiagnostic(PrimaryLocalizationFileNotFoundDescriptor, primaryFileName));
        }
        return new LocalizationGenerationState(targetNamespace, orderedFiles, primaryFile, diagnostics.ToImmutable());
    }

    private static LocalizationFileCandidate? CreateLocalizationFileCandidate(AdditionalText file)
    {
        var fileName = Path.GetFileName(file.Path);
        var cultureName = Path.GetFileNameWithoutExtension(file.Path);
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(cultureName))
        {
            return null;
        }

        return new LocalizationFileCandidate(
            file.Path,
            fileName,
            cultureName,
            file.GetText()?.ToString() ?? string.Empty);
    }

    private static string GetLocalizationFileName(AnalyzerConfigOptionsProvider provider)
    {
        if (provider.GlobalOptions.TryGetValue(LocalizationFileProperty, out var fileName) && !string.IsNullOrWhiteSpace(fileName))
        {
            return fileName;
        }

        return "en.json";
    }

    private static bool IsLocalizationJsonFile(string path) =>
        string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);
}
}
