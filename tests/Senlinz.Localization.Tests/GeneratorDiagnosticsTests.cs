using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Senlinz.Localization;

namespace Senlinz.Localization.Tests;

public class GeneratorDiagnosticsTests
{
    [Fact]
    public void Reports_invalid_json_diagnostic()
    {
        var diagnostics = RunGenerator(new Dictionary<string, string>
        {
            ["/tmp/Sample/L/en.json"] = "{ \"hello\": }"
        });

        var diagnostic = Assert.Single(diagnostics.Where(static diagnostic => diagnostic.Id == "SL001"));
        Assert.Contains("could not be parsed", diagnostic.GetMessage());
    }

    [Fact]
    public void Reports_conflicting_identifier_diagnostic()
    {
        var diagnostics = RunGenerator(new Dictionary<string, string>
        {
            ["/tmp/Sample/L/en.json"] = """
                                       {
                                        "user_status": "A",
                                        "user-status": "B"
                                      }
                                      """
        });

        var diagnostic = Assert.Single(diagnostics.Where(static diagnostic => diagnostic.Id == "SL003"));
        Assert.Contains("UserStatus", diagnostic.GetMessage());
    }

    [Fact]
    public void Generated_sources_compile_with_csharp8()
    {
        var diagnostics = RunGeneratorAndGetCompilationDiagnostics(
            new Dictionary<string, string>
            {
                ["/tmp/Sample/L/en.json"] = """
                                           {
                                             "hello": "Hello",
                                             "userType": {
                                               "teacher": "Teacher"
                                             }
                                           }
                                           """
            },
            LanguageVersion.CSharp8);

        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static ImmutableArray<Diagnostic> RunGenerator(IReadOnlyDictionary<string, string> files)
    {
        return RunGenerator(files, LanguageVersion.CSharp12).Diagnostics;
    }

    private static ImmutableArray<Diagnostic> RunGeneratorAndGetCompilationDiagnostics(
        IReadOnlyDictionary<string, string> files,
        LanguageVersion languageVersion)
    {
        var result = RunGenerator(files, languageVersion);
        return result.Compilation.GetDiagnostics();
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, Compilation Compilation) RunGenerator(
        IReadOnlyDictionary<string, string> files,
        LanguageVersion languageVersion)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(languageVersion);
        var compilation = CSharpCompilation.Create(
            assemblyName: "Sample",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText("namespace Sample { public sealed class Marker { } }", parseOptions)
            ],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var additionalTexts = files.Select(static file => (AdditionalText)new InMemoryAdditionalText(file.Key, file.Value)).ToImmutableArray();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new LGenerator().AsSourceGenerator()],
            additionalTexts: additionalTexts,
            parseOptions: parseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.RootNamespace"] = "Sample",
                ["build_property.SenlinzLocalizationFile"] = "en.json"
            }));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        return (driver.GetRunResult().Diagnostics, outputCompilation);
    }

    private static ImmutableArray<MetadataReference> GetMetadataReferences()
    {
        var platformReferences = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var projectReferences = new[]
        {
            MetadataReference.CreateFromFile(typeof(LGenerator).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(LString).Assembly.Location)
        };

        return platformReferences
            .Concat(projectReferences)
            .GroupBy(static reference => ((PortableExecutableReference)reference).FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .Cast<MetadataReference>()
            .ToImmutableArray();
    }

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public InMemoryAdditionalText(string path, string text)
        {
            Path = path;
            _text = SourceText.From(text, Encoding.UTF8);
        }

        public override string Path { get; }

        public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default) => _text;
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions EmptyOptions = new TestAnalyzerConfigOptions(new Dictionary<string, string>());
        private readonly AnalyzerConfigOptions _globalOptions;

        public TestAnalyzerConfigOptionsProvider(IReadOnlyDictionary<string, string> globalOptions)
        {
            _globalOptions = new TestAnalyzerConfigOptions(globalOptions);
        }

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => EmptyOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => EmptyOptions;
    }

    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        public TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string> values)
        {
            _values = values;
        }

        public override bool TryGetValue(string key, out string value)
        {
            if (_values.TryGetValue(key, out var resolvedValue))
            {
                value = resolvedValue;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
