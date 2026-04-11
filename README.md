# Senlinz.Localization

**English** | [Chinese](./README.zh-CN.md)

A JSON-driven localization source generator for .NET that generates strongly typed localization accessors, resource base classes, and enum-to-localization helpers.

Supports .NET 6 and newer consumer projects.

## Features

- Generate `L` accessors from `l.json`.
- Generate `LResource` base classes for culture-specific resource implementations.
- Resolve localized text through `LString`, `LStringResolver`, and `LResourceProvider`.
- Convert enum values to localization keys with `[LString]` and `[LStringKey]`.
- Publish `Senlinz.Localization` and `Senlinz.Localization.Abstractions` as separate NuGet packages.

## Package selection

### `Senlinz.Localization`

Use this package in consumer projects that need source generation from JSON.

```bash
dotnet add package Senlinz.Localization --prerelease
```

### `Senlinz.Localization.Abstractions`

Use this package only when you need the shared runtime contracts and helpers without the source generator.

```bash
dotnet add package Senlinz.Localization.Abstractions --prerelease
```

## Quick start

### 1. Create the localization file

Create `l.json` in your project root.

```json
{
  "hello": "Hello",
  "sayHelloTo": "Hello {name}!",
  "statusReady": "Ready",
  "UserType_Teacher": "Teacher",
  "UserType_Student": "Student"
}
```

### 2. Register the file in the project

```xml
<ItemGroup>
  <AdditionalFiles Include="l.json" />
  <None Update="l.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- `AdditionalFiles` lets the source generator read the file during compilation.
- `CopyToOutputDirectory` is useful when the application also wants to ship the JSON file.

### 3. Use generated members

After build, the generator creates strongly typed members from each JSON key.

```csharp
Console.WriteLine(L.Hello);
Console.WriteLine(L.SayHelloTo("World"));
```

- `hello` becomes `L.Hello`.
- `sayHelloTo` becomes `L.SayHelloTo(string name)`.

## Localization file rules

### Key format

- JSON keys are converted into generated C# member names.
- Keep keys stable because generated API names depend on them.

### Placeholder parameters

Placeholders inside values become method parameters.

```json
{
  "welcomeUser": "Welcome {userName}",
  "orderSummary": "Order {orderId} for {customerName}"
}
```

Generated usage:

```csharp
var message1 = L.WelcomeUser("Alice");
var message2 = L.OrderSummary("SO-001", "Alice");
```

### Escaping placeholders

- If you want to keep braces as literal text instead of generating a parameter, prefix the placeholder name with `$`.

```json
{
  "templateTip": "Use {$name} as a placeholder in your template."
}
```

- The generated default text becomes `Use {name} as a placeholder in your template.`

### Custom file name

If you do not want to use `l.json`, set `Mo.Localization.File`.

```ini
is_global = true

build_property.Mo.Localization.File = localization.json
```

```xml
<ItemGroup>
  <GlobalAnalyzerConfigFiles Include=".globalconfig" />
  <AdditionalFiles Include="localization.json" />
  <None Update="localization.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

The legacy `MoLocalizationFile` MSBuild property is still supported for compatibility.

## Generated types

### `L`

- `L` contains strongly typed accessors for every key in the localization JSON.
- Plain values generate properties, and values with placeholders generate methods.

### `LResource`

- `LResource` is a generated abstract base class with one protected abstract member per localization key.
- Implement one derived class per culture.

Example:

```csharp
using Senlinz.Localization;

public sealed class EnResource : LResource
{
    public override string Culture => "en";

    protected override string Hello => "Hello";
    protected override string SayHelloTo => "Hello {name}!";
    protected override string StatusReady => "Ready";
}

public sealed class ZhResource : LResource
{
    public override string Culture => "zh";

    protected override string Hello => "你好";
    protected override string SayHelloTo => "你好，{name}！";
    protected override string StatusReady => "就绪";
}
```

### `LString`

- `LString` carries the localization key, fallback text, and runtime arguments.
- You normally get `LString` values from generated `L` members or from enum extensions.

## Resolve localized values

Use `LResourceProvider` to hold resources and `LStringResolver` to resolve text for the current culture.

```csharp
using Senlinz.Localization;

var currentCulture = "zh";
var provider = new LResourceProvider(new EnResource(), new ZhResource());
var resolver = new LStringResolver(() => currentCulture, provider.GetResource);

Console.WriteLine(resolver[L.Hello]);
Console.WriteLine(resolver[L.SayHelloTo("世界")]);
```

You can also call the extension method:

```csharp
var text = resolver.Resolve(L.Hello);
```

### Fallback behavior

- If no resource exists for the current culture, the default text from `l.json` is used.
- If a resource exists but does not contain a key, the default text is also used.

## Enum localization

### `[LString]`

Apply `[LString]` to an enum to generate a `ToLString()` extension method.

```csharp
[LString]
public enum UserType
{
    Teacher,
    Student
}
```

- This generates `UserTypeExtensions.ToLString(this UserType value)`.
- By default, the generated key pattern is `<EnumName>_<MemberName>`.

For the enum above, the expected localization keys are typically:

```json
{
  "UserType_Teacher": "Teacher",
  "UserType_Student": "Student"
}
```

### `[LStringKey]`

Use `[LStringKey]` on enum members when you want to map to an existing localization key.

```csharp
[LString]
public enum UserType
{
    [LStringKey("teacher")]
    Teacher,

    [LStringKey("student")]
    Student
}
```

`[LStringKey]` replaces only the enum member portion of the generated key. The enum prefix and separator are still kept.

Matching JSON:

```json
{
  "UserType_teacher": "Teacher",
  "UserType_student": "Student"
}
```

If you pass the full key explicitly, it is used as-is and the prefix is not duplicated:

```csharp
[LString]
public enum UserType
{
    [LStringKey("UserType_Teacher")]
    Teacher,

    [LStringKey("UserType_Student")]
    Student
}
```

Usage:

```csharp
var text = UserType.Student.ToLString();
Console.WriteLine(resolver[text]);
```

### Custom separator

`LStringAttribute` accepts an optional separator value.

```csharp
[LString("_")]
public enum OrderStatus
{
    Pending,
    Completed
}
```

- Choose a separator that keeps the generated member name valid in C#, such as `_`.
- Use `[LStringKey]` when you want to customize the enum member segment while keeping the enum prefix.

## End-to-end example

### `l.json`

```json
{
  "hello": "Hello",
  "sayHelloTo": "Hello {name}!",
  "UserType_Teacher": "Teacher",
  "UserType_Student": "Student"
}
```

### Enum

```csharp
[LString]
public enum UserType
{
    Teacher,
    Student
}
```

### Resources and resolver

```csharp
using Senlinz.Localization;

var currentCulture = "zh";
var provider = new LResourceProvider(new ZhResource());
var resolver = new LStringResolver(() => currentCulture, provider.GetResource);

Console.WriteLine(resolver[L.Hello]);
Console.WriteLine(resolver[L.SayHelloTo("世界")]);
Console.WriteLine(resolver[UserType.Student.ToLString()]);

public sealed class ZhResource : LResource
{
    public override string Culture => "zh";

    protected override string Hello => "你好";
    protected override string SayHelloTo => "你好，{name}！";
    protected override string UserTypeTeacher => "老师";
    protected override string UserTypeStudent => "学生";
}
```

Expected output:

```text
你好
你好，世界！
学生
```

## Release packages

- Create and push a version tag such as `v1.0.0` to trigger the publish workflow.

1. Validate the solution.
   ```bash
   dotnet test Senlinz.Localization.slnx --configuration Release
   ```
2. Pack locally if needed.
   ```bash
   dotnet pack Senlinz.Localization.slnx --configuration Release --output artifacts
   ```
3. The GitHub Actions workflow publishes both packages to NuGet when the tag build succeeds.
