# Senlinz.Localization

Supports .NET 6 and newer consumer projects.

## Features

- `LString` runtime helpers for resolving localized text
- JSON-driven source generator that creates `L` and `LResource` types
- Enum-to-localization helpers with `[LString]` and `[LStringKey]`
- Separate `Senlinz.Localization` and `Senlinz.Localization.Abstractions` NuGet packages

## Install

```bash
dotnet add package Senlinz.Localization
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

## Release packages

Create and push a version tag such as `v1.0.0` to trigger the publish workflow.

1. Validate the solution:
   ```bash
   dotnet test Senlinz.Localization.slnx --configuration Release
   ```
2. Create release packages locally when needed:
   ```bash
   dotnet pack Senlinz.Localization.slnx --configuration Release --output artifacts
   ```
3. The GitHub Actions workflow publishes both `Senlinz.Localization` and `Senlinz.Localization.Abstractions` to NuGet when the tag build succeeds.

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
