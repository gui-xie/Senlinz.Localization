# Senlinz.Localization

Single-project localization package extracted from the localization support in `gui-xie/Senlin.Mo`.

## Features

- `LString` runtime helpers for resolving localized text
- JSON-driven source generator that creates `L` and `LResource` types
- Enum-to-localization helpers with `[LString]` and `[LStringKey]`
- NuGet packing support from one project

## Install

```bash
dotnet add package Senlinz.Localization --prerelease
```

## Define localization keys

Create `l.json` in the consuming project:

```json
{
  "hello": "Hello",
  "sayHelloTo": "Hello {name}!"
}
```

Register it as an additional file:

```xml
<ItemGroup>
  <AdditionalFiles Include="l.json" />
  <None Update="l.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Optional custom file name:

```xml
<PropertyGroup>
  <MoLocalizationFile>localization.json</MoLocalizationFile>
</PropertyGroup>
<ItemGroup>
  <CompilerVisibleProperty Include="MoLocalizationFile" />
</ItemGroup>
```

## Publish beta packages

The package is currently published as a beta release.

1. Add the `NUGET_API_KEY` repository secret.
2. Push a tag such as `v1.0.0-beta.1`.
3. GitHub Actions will build, test, pack, and publish the package to NuGet automatically.

## Use generated types

```csharp
using Senlinz.Localization;

var provider = new LResourceProvider(new ZhResource());
var currentCulture = "zh";
var resolver = new LStringResolver(() => currentCulture, provider.GetResource);

Console.WriteLine(resolver[L.Hello]);
Console.WriteLine(resolver[L.SayHelloTo("World")]);

public sealed class ZhResource : LResource
{
    public override string Culture => "zh";

    protected override string Hello => "你好";

    protected override string SayHelloTo => "你好，{name}！";
}
```
