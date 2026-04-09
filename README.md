# Senlinz.Localization

Supports .NET 6 and newer consumer projects.

## Features

- `LString` runtime helpers for resolving localized text
- JSON-driven source generator that creates `L` and `LResource` types
- Enum-to-localization helpers with `[LString]` and `[LStringKey]`
- NuGet packing support from one project

## Install

```bash
dotnet add package Senlinz.Localization --prerelease
```

`Senlinz.Localization` now ships together with `Senlinz.Localization.Abstractions`, and you can install the abstractions package separately when you only need the shared contracts/runtime helpers.

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

1. Build the package:
   ```bash
   dotnet pack src/Senlinz.Localization/Senlinz.Localization.csproj --configuration Release
   ```
2. Publish the generated `.nupkg` manually when you are ready for the next beta release.

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
