# Senlinz.Localization

[English](./README.md) | **中文**

A JSON-driven localization source generator for .NET that generates strongly typed localization accessors, resource base classes, and enum-to-localization helpers.  
一个面向 .NET 的基于 JSON 的本地化源码生成器，用于生成强类型本地化访问器、资源基类以及枚举本地化辅助方法。

Supports .NET 6 and newer consumer projects.  
支持 .NET 6 及以上的消费项目。

## Features | 功能特性

- **English**: Generate `L` accessors from `l.json`.
- **中文**：从 `l.json` 生成 `L` 访问器。
- **English**: Generate `LResource` base classes for culture-specific resource implementations.
- **中文**：生成 `LResource` 基类，方便实现不同语言资源。
- **English**: Resolve localized text through `LString`, `LStringResolver`, and `LResourceProvider`.
- **中文**：通过 `LString`、`LStringResolver` 和 `LResourceProvider` 解析本地化文本。
- **English**: Convert enum values to localization keys with `[LString]` and `[LStringKey]`.
- **中文**：通过 `[LString]` 与 `[LStringKey]` 将枚举值转换为本地化键。
- **English**: Publish `Senlinz.Localization` and `Senlinz.Localization.Abstractions` as separate NuGet packages.
- **中文**：`Senlinz.Localization` 与 `Senlinz.Localization.Abstractions` 可分别作为 NuGet 包发布。

## Package selection | 包选择

### `Senlinz.Localization`

- **English**: Use this package in consumer projects that need source generation from JSON.
- **中文**：如果你的项目需要从 JSON 自动生成本地化代码，请安装这个包。

```bash
dotnet add package Senlinz.Localization --prerelease
```

### `Senlinz.Localization.Abstractions`

- **English**: Use this package only when you need the shared runtime contracts and helpers without the source generator.
- **中文**：如果你只需要运行时契约与辅助类型，而不需要源码生成器，请安装这个包。

```bash
dotnet add package Senlinz.Localization.Abstractions --prerelease
```

## Quick start | 快速开始

### 1. Create the localization file | 创建本地化文件

Create `l.json` in your project root.  
在项目根目录创建 `l.json`。

```json
{
  "hello": "Hello",
  "sayHelloTo": "Hello {name}!",
  "statusReady": "Ready",
  "UserType_Teacher": "Teacher",
  "UserType_Student": "Student"
}
```

### 2. Register the file in the project | 在项目中注册文件

```xml
<ItemGroup>
  <AdditionalFiles Include="l.json" />
  <None Update="l.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- **English**: `AdditionalFiles` lets the source generator read the file during compilation.
- **中文**：`AdditionalFiles` 让源码生成器在编译期间读取该文件。
- **English**: `CopyToOutputDirectory` is useful when the application also wants to ship the JSON file.
- **中文**：`CopyToOutputDirectory` 适用于运行时也希望将 JSON 文件一并输出的场景。

### 3. Use generated members | 使用生成代码

After build, the generator creates strongly typed members from each JSON key.  
构建后，生成器会根据每个 JSON 键生成强类型成员。

```csharp
Console.WriteLine(L.Hello);
Console.WriteLine(L.SayHelloTo("World"));
```

- **English**: `hello` becomes `L.Hello`.
- **中文**：`hello` 会生成 `L.Hello`。
- **English**: `sayHelloTo` becomes `L.SayHelloTo(string name)`.
- **中文**：`sayHelloTo` 会生成 `L.SayHelloTo(string name)`。

## Localization file rules | 本地化文件规则

### Key format | 键格式

- **English**: JSON keys are converted into generated C# member names.
- **中文**：JSON 键会被转换为生成的 C# 成员名。
- **English**: Keep keys stable because generated API names depend on them.
- **中文**：请保持键名稳定，因为生成的 API 名称依赖这些键。

### Placeholder parameters | 占位符参数

Placeholders inside values become method parameters.  
值中的占位符会生成方法参数。

```json
{
  "welcomeUser": "Welcome {userName}",
  "orderSummary": "Order {orderId} for {customerName}"
}
```

Generated usage:  
生成后的调用方式：

```csharp
var message1 = L.WelcomeUser("Alice");
var message2 = L.OrderSummary("SO-001", "Alice");
```

### Escaping placeholders | 转义占位符

- **English**: If you want to keep braces as literal text instead of generating a parameter, prefix the placeholder name with `$`.
- **中文**：如果你希望保留花括号文本而不是生成参数，请在占位符名称前加 `$`。

```json
{
  "templateTip": "Use {$name} as a placeholder in your template."
}
```

- **English**: The generated default text becomes `Use {name} as a placeholder in your template.`
- **中文**：生成后的默认文本会是 `Use {name} as a placeholder in your template.`

### Custom file name | 自定义文件名

If you do not want to use `l.json`, set `MoLocalizationFile`.  
如果你不想使用 `l.json`，可以设置 `MoLocalizationFile`。

```xml
<PropertyGroup>
  <MoLocalizationFile>localization.json</MoLocalizationFile>
</PropertyGroup>
<ItemGroup>
  <CompilerVisibleProperty Include="MoLocalizationFile" />
  <AdditionalFiles Include="localization.json" />
  <None Update="localization.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

## Generated types | 生成的类型

### `L`

- **English**: `L` contains strongly typed accessors for every key in the localization JSON.
- **中文**：`L` 包含来自本地化 JSON 的所有强类型访问器。
- **English**: Plain values generate properties, and values with placeholders generate methods.
- **中文**：普通文本会生成属性，带占位符的文本会生成方法。

### `LResource`

- **English**: `LResource` is a generated abstract base class with one protected abstract member per localization key.
- **中文**：`LResource` 是自动生成的抽象基类，每个本地化键都会对应一个受保护的抽象成员。
- **English**: Implement one derived class per culture.
- **中文**：通常为每种语言实现一个派生类。

Example | 示例：

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

- **English**: `LString` carries the localization key, fallback text, and runtime arguments.
- **中文**：`LString` 保存本地化键、默认回退文本以及运行时参数。
- **English**: You normally get `LString` values from generated `L` members or from enum extensions.
- **中文**：通常你会从生成的 `L` 成员或枚举扩展方法中得到 `LString`。

## Resolve localized values | 解析本地化值

Use `LResourceProvider` to hold resources and `LStringResolver` to resolve text for the current culture.  
使用 `LResourceProvider` 保存资源，并通过 `LStringResolver` 按当前语言解析文本。

```csharp
using Senlinz.Localization;

var currentCulture = "zh";
var provider = new LResourceProvider(new EnResource(), new ZhResource());
var resolver = new LStringResolver(() => currentCulture, provider.GetResource);

Console.WriteLine(resolver[L.Hello]);
Console.WriteLine(resolver[L.SayHelloTo("世界")]);
```

You can also call the extension method:  
也可以使用扩展方法：

```csharp
var text = resolver.Resolve(L.Hello);
```

### Fallback behavior | 回退行为

- **English**: If no resource exists for the current culture, the default text from `l.json` is used.
- **中文**：如果当前语言没有对应资源，则会使用 `l.json` 中的默认文本。
- **English**: If a resource exists but does not contain a key, the default text is also used.
- **中文**：如果资源存在但缺少某个键，同样会回退到默认文本。

## Enum localization | 枚举本地化

### `[LString]`

Apply `[LString]` to an enum to generate a `ToLString()` extension method.  
给枚举加上 `[LString]` 后，会生成 `ToLString()` 扩展方法。

```csharp
[LString]
public enum UserType
{
    Teacher,
    Student
}
```

- **English**: This generates `UserTypeExtensions.ToLString(this UserType value)`.
- **中文**：这会生成 `UserTypeExtensions.ToLString(this UserType value)`。
- **English**: By default, the generated key pattern is `<EnumName>_<MemberName>`.
- **中文**：默认生成的键模式为 `<枚举名>_<成员名>`。

For the enum above, the expected localization keys are typically:  
对于上面的枚举，通常对应的本地化键是：

```json
{
  "UserType_Teacher": "Teacher",
  "UserType_Student": "Student"
}
```

### `[LStringKey]`

Use `[LStringKey]` on enum members when you want to map to an existing localization key.  
如果你希望枚举成员映射到现有键，请在成员上使用 `[LStringKey]`。

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

- **English**: By default, `UserType.Student.ToLString()` resolves through the prefixed localization key `UserType_Student`, but `[LStringKey]` overrides that mapping for the annotated members.
- **中文**：默认情况下，`UserType.Student.ToLString()` 会通过带前缀的本地化键 `UserType_Student` 进行解析，但带有 `[LStringKey]` 的成员会覆盖这个默认映射。

Matching JSON:  
对应的 JSON：

```json
{
  "teacher": "Teacher",
  "student": "Student"
}
```

Usage | 用法：

```csharp
var text = UserType.Student.ToLString();
Console.WriteLine(resolver[text]);
```

### Custom separator | 自定义分隔符

`LStringAttribute` accepts an optional separator value.  
`LStringAttribute` 支持可选的分隔符参数。

```csharp
[LString("_")]
public enum OrderStatus
{
    Pending,
    Completed
}
```

- **English**: Choose a separator that keeps the generated member name valid in C#, such as `_`.
- **中文**：请使用能保证生成成员名仍然是合法 C# 标识符的分隔符，例如 `_`。
- **English**: Use `[LStringKey]` when you need full control over the mapped localization key.
- **中文**：如果你需要完全控制映射到哪个本地化键，建议直接使用 `[LStringKey]`。

## End-to-end example | 完整示例

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

Expected output | 预期输出：

```text
你好
你好，世界！
学生
```

## Release packages | 发布包

- **English**: Create and push a version tag such as `v1.0.0` to trigger the publish workflow.
- **中文**：创建并推送类似 `v1.0.0` 的版本标签即可触发发布工作流。

1. **English**: Validate the solution.  
   **中文**：先验证解决方案。
   ```bash
   dotnet test Senlinz.Localization.slnx --configuration Release
   ```
2. **English**: Pack locally if needed.  
   **中文**：如有需要，可在本地打包。
   ```bash
   dotnet pack Senlinz.Localization.slnx --configuration Release --output artifacts
   ```
3. **English**: The GitHub Actions workflow publishes both packages to NuGet when the tag build succeeds.  
   **中文**：标签构建成功后，GitHub Actions 会将两个包一起发布到 NuGet。
