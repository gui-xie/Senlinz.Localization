# Senlinz.Localization

**English** | [Chinese](./README.zh-CN.md)

A JSON-driven localization source generator for .NET that generates strongly typed localization accessors, resource base classes, and enum-to-localization helpers.

Supports .NET 6 and newer consumer projects.

- Documentation site: <https://gui-xie.github.io/Senlinz.Localization/>
- Current package version: `3.1.0`
- Release notes: [RELEASE_NOTES.md](./RELEASE_NOTES.md)
- Changelog: [CHANGELOG.md](./CHANGELOG.md)

## Quick navigation

- [Features](#features)
- [Quick start](#quick-start)
- [Generated types](#generated-types)
- [Resolve localized values](#resolve-localized-values)
- [Enum localization](#enum-localization)
- [End-to-end example](#end-to-end-example)

## Features

- Generate `L` accessors from a primary culture JSON file.
- Generate `LResource` plus one concrete resource class for every discovered culture JSON file.
- Resolve localized text through `LString`, `LStringResolver`, and generated `LResource` types.
- Convert enum values to localization keys with `[LString]` and `[LStringKey]`.
- Publish `Senlinz.Localization` and `Senlinz.Localization.Abstractions` as separate NuGet packages.

## Package selection

### `Senlinz.Localization`

Use this package in consumer projects that need source generation from JSON.

```bash
dotnet add package Senlinz.Localization
```

### `Senlinz.Localization.Abstractions`

Use this package only when you need the shared localization contracts without the source generator.

```bash
dotnet add package Senlinz.Localization.Abstractions
```

## Quick start

### 1. Create the localization files

Place localization JSON files under the `L/` folder. `en.json` is the default primary file unless you override it with `SenlinzLocalizationFile`.

Example layout:

```text
MyProject/
├── L/
│   ├── en.json
│   └── zh.json
└── MyProject.csproj
```

```json
// en.json
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

```json
// zh.json
{
  "hello": "你好",
  "sayHelloTo": "你好，{name}！",
  "statusReady": "就绪",
  "userType": {
    "teacher": "老师",
    "student": "学生"
  }
}
```

### 2. Register the files in the project

```xml
<ItemGroup>
  <AdditionalFiles Include="L/*.json" />
</ItemGroup>
```

- `AdditionalFiles` lets the source generator read the localization files under `L/`.
- If you later place files into subfolders, just widen the glob pattern; folders do not affect the generated namespace.

### 3. Use generated members

After build, the generator creates strongly typed members from each JSON key.

```csharp
Console.WriteLine(L.Hello);
Console.WriteLine(L.SayHelloTo("World"));
```

- `hello` becomes `L.Hello`.
- `sayHelloTo` becomes `L.SayHelloTo(string name)`.

### 4. Use generated culture resources and resolve text

```csharp
using Senlinz.Localization;

var currentCulture = "zh";
var resolver = new LStringResolver(() => currentCulture);

Console.WriteLine(resolver[L.Hello]);
Console.WriteLine(resolver[L.SayHelloTo("世界")]);
```

- `new LStringResolver(() => currentCulture)` uses the generated resolver and automatically includes all discovered resources.
- If you need runtime overrides, pass your own `LResource` instances to the overload that accepts resources explicitly.

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

### Primary localization file

`SenlinzLocalizationFile` selects which JSON file generates `L` and which generated resource acts as the default resource. The default is `en.json`.

```xml
<PropertyGroup>
  <SenlinzLocalizationFile>zh.json</SenlinzLocalizationFile>
</PropertyGroup>

<ItemGroup>
  <AdditionalFiles Include="L/*.json" />
</ItemGroup>
```

## Generated types

### `L`

- `L` contains strongly typed accessors for every key in the localization JSON.
- Plain values generate properties, and values with placeholders generate methods.

### `LResource`

- `LResource` is a generated abstract base class whose `GetResource()` method returns the primary localization dictionary.
- The generator also emits one concrete internal `*Resource` class per discovered culture JSON file, such as `EnResource` and `ZhResource`.
- `new LStringResolver(() => currentCulture)` uses the generated resolver and wires in every discovered resource automatically.
- You can still derive your own custom resources from `LResource` and override `GetResource()` when you need runtime overrides.

### `LString`

- `LString` carries the localization key, fallback text, and runtime arguments.
- You normally get `LString` values from generated `L` members or from enum extensions.

## Resolve localized values

Use `LStringResolver` to resolve text for the current culture. For most applications, `new LStringResolver(...)` is the simplest setup.

```csharp
using Senlinz.Localization;

var currentCulture = "zh";
var resolver = new LStringResolver(() => currentCulture);

Console.WriteLine(resolver[L.Hello]);
Console.WriteLine(resolver[L.SayHelloTo("世界")]);
```

If you need runtime overrides, derive from `LResource`, override `GetResource()`, and pass your own resources explicitly.

You can also call the instance method:

```csharp
var text = resolver.Resolve(L.Hello);
```

### Fallback behavior

- If no resource exists for the current culture, the default text from the primary JSON file is used.
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

### `en.json`

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

### `zh.json`

```json
{
  "hello": "你好",
  "sayHelloTo": "你好，{name}！",
  "userType": {
    "teacher": "老师",
    "student": "学生"
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

### Resolver

```csharp
using Senlinz.Localization;

var currentCulture = "zh";
var resolver = new LStringResolver(() => currentCulture);

Console.WriteLine(resolver[L.Hello]);
Console.WriteLine(resolver[L.SayHelloTo("世界")]);
Console.WriteLine(resolver[UserType.Student.ToLString()]);
```

Expected output:

```text
你好
你好，世界！
学生
```
