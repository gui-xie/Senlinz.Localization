# Senlinz.Localization

**English** | [Chinese](./README.zh-CN.md)

A JSON-driven localization source generator for .NET that generates strongly typed localization accessors, resource base classes, and enum-to-localization helpers.

Supports .NET 6 and newer consumer projects.

- Documentation site: <https://gui-xie.github.io/Senlinz.Localization/>
- Current package version: `1.1.1`

## Quick navigation

- [Features](#features)
- [Quick start](#quick-start)
- [Generated types](#generated-types)
- [Resolve localized values](#resolve-localized-values)
- [Enum localization](#enum-localization)
- [End-to-end example](#end-to-end-example)

## Features

- Generate `L` accessors from `l.json`.
- Generate `LResource` base classes for culture-specific resource implementations.
- Resolve localized text through `LString`, `LStringResolver`, and `LResourceProvider`.
- Convert enum values to localization keys with `[LString]` and `[LStringKey]`.
- Publish `Senlinz.Localization` and `Senlinz.Localization.Abstractions` as separate NuGet packages with a shared embedded package icon.

## Package selection

### `Senlinz.Localization`

Use this package in consumer projects that need source generation from JSON.

```bash
dotnet add package Senlinz.Localization
```

### `Senlinz.Localization.Abstractions`

Use this package only when you need the shared runtime contracts and helpers without the source generator.

```bash
dotnet add package Senlinz.Localization.Abstractions
```

## Quick start

### 1. Create the localization file

Create `l.json` in your project root.

```json
{
  "hello": "Hello",
  "sayHelloTo": "Hello {name}!",
  "statusReady": "Ready",
  "userType": {
    "teacher": "Teacher",
    "student": "Student"
  }
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

### 4. Create culture resources and resolve text

```csharp
using Senlinz.Localization;

var currentCulture = "zh";
var resolver = new LStringResolver(
    () => currentCulture,
    new EnResource(),
    new ZhResource());

Console.WriteLine(resolver[L.Hello]);
Console.WriteLine(resolver[L.SayHelloTo("世界")]);
```

- Pass resources directly to `LStringResolver` for the common case.
- If you already manage resources elsewhere, you can still pass an existing `LResourceProvider`.

## Localization file rules

### Key format

- JSON keys are converted into generated C# member names.
- Keep keys stable because generated API names depend on them.
- Generated member names follow the JSON shape directly and only capitalize the leading letter to fit Pascal-style naming, so `user_status` becomes `L.User_status`.
- Nested JSON objects generate nested accessors, so `exception -> user -> notFound` becomes `L.Exception.User.NotFound(...)`.
- Nested JSON paths use dotted keys internally, so the example above resolves as `exception.user.notFound`.
- Enum keys use a nested path based on the enum name and member name, so `UserType.Teacher` resolves to `userType.teacher` and `L.UserType.Teacher`.

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

If you do not want to use `l.json`, set `SenlinzLocalizationFile` in your project file.

```xml
<PropertyGroup>
  <SenlinzLocalizationFile>localization.json</SenlinzLocalizationFile>
</PropertyGroup>

<ItemGroup>
  <AdditionalFiles Include="localization.json" />
  <None Update="localization.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

## Generated types

### `L`

- `L` contains strongly typed accessors for every key in the localization JSON.
- Plain values generate properties, and values with placeholders generate methods.

### `LResource`

- `LResource` is a generated abstract base class with one protected virtual member per top-level localization key.
- Each generated member returns the default value from the localization JSON unless a derived resource overrides it.
- Implement one derived class per culture and override only the values that differ.

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
}

public sealed class FrResource : LResource
{
    public override string Culture => "fr";

    protected override string Hello => "Bonjour";
}
```

### `LString`

- `LString` carries the localization key, fallback text, and runtime arguments.
- You normally get `LString` values from generated `L` members or from enum extensions.

## Resolve localized values

Use `LStringResolver` to resolve text for the current culture. For most applications, passing resources directly is the simplest setup.

```csharp
using Senlinz.Localization;

var currentCulture = "zh";
var resolver = new LStringResolver(() => currentCulture, new EnResource(), new ZhResource());

Console.WriteLine(resolver[L.Hello]);
Console.WriteLine(resolver[L.SayHelloTo("世界")]);
```

If you already have a provider instance, you can pass it directly:

```csharp
var provider = new LResourceProvider(new EnResource(), new ZhResource());
var resolver = new LStringResolver(() => currentCulture, provider);
```

You can also call the extension method:

```csharp
var text = resolver.Resolve(L.Hello);
```

### Fallback behavior

- If no resource exists for the current culture, the default text from `l.json` is used.
- If a resource exists but does not contain a key, the default text is also used.
- Resource dictionaries are cached per resolver instance and culture.

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
- Enum values always use the nested key pattern `<enumNameCamelCase>.<memberNameCamelCase>`.
- For `UserType.Teacher`, the generated key is `userType.teacher`, so the accessor is `L.UserType.Teacher`.

For the enum above, the expected localization keys are typically:

```json
{
  "userType": {
    "teacher": "Teacher",
    "student": "Student"
  }
}
```

### `[LStringKey]`

Use `[LStringKey]` on enum members when you want to override only the enum member segment of the key.

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

`[LStringKey]` only replaces the final enum member segment. The enum prefix segment stays derived from the enum name.

Matching JSON:

```json
{
  "userType": {
    "teacher": "Teacher",
    "student": "Student"
  }
}
```

Passing a dotted or legacy full key still only changes the final member segment:

```csharp
[LString]
public enum UserType
{
    [LStringKey("userType.teacher")]
    Teacher,

    [LStringKey("legacy.student")]
    Student
}
```

Usage:

```csharp
var text = UserType.Student.ToLString();
Console.WriteLine(resolver[text]);
```

## End-to-end example

### `l.json`

```json
{
  "hello": "Hello",
  "sayHelloTo": "Hello {name}!",
  "userType": {
    "teacher": "Teacher",
    "student": "Student"
  }
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
var resolver = new LStringResolver(() => currentCulture, new ZhResource());

Console.WriteLine(resolver[L.Hello]);
Console.WriteLine(resolver[L.SayHelloTo("世界")]);
Console.WriteLine(resolver[UserType.Student.ToLString()]);

public sealed class ZhResource : LResource
{
    private const string UserTypeTeacherKey = "userType.teacher";
    private const string UserTypeStudentKey = "userType.student";

    public override string Culture => "zh";

    protected override string Hello => "你好";
    protected override string SayHelloTo => "你好，{name}！";

    public override Dictionary<string, string> GetResource()
    {
        var resource = base.GetResource();
        resource[UserTypeTeacherKey] = "老师";
        resource[UserTypeStudentKey] = "学生";
        return resource;
    }
}
```

Expected output:

```text
你好
你好，世界！
学生
```
