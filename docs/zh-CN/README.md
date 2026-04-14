# Senlinz.Localization

**中文** | [English](../README.md)

一个面向 .NET 的基于 JSON 的本地化源码生成器，用于生成强类型本地化访问器、资源基类以及枚举本地化辅助方法。

支持 .NET 6 及以上的消费项目。

- 文档站点：<https://gui-xie.github.io/Senlinz.Localization/>
- 当前包版本：`2.0.0`

## 快速导航

- [功能特性](#功能特性)
- [快速开始](#快速开始)
- [生成的类型](#生成的类型)
- [解析本地化值](#解析本地化值)
- [枚举本地化](#枚举本地化)
- [完整示例](#完整示例)
- [发布与文档站点](#发布与文档站点)

## 功能特性

- 从 `l.json` 生成 `L` 访问器。
- 生成 `LResource` 基类，方便实现不同语言资源。
- 通过 `LString`、`LStringResolver` 和生成的 `LResource` 类型解析本地化文本。
- 通过 `[LString]` 与 `[LStringKey]` 将枚举值转换为本地化键。
- `Senlinz.Localization` 与 `Senlinz.Localization.Abstractions` 可分别作为带共享嵌入图标的 NuGet 包发布。

## 包选择

### `Senlinz.Localization`

- 如果你的项目需要从 JSON 自动生成本地化代码，请安装这个包。

```bash
dotnet add package Senlinz.Localization
```

### `Senlinz.Localization.Abstractions`

- 如果你只需要运行时契约与辅助类型，而不需要源码生成器，请安装这个包。

```bash
dotnet add package Senlinz.Localization.Abstractions
```

## 快速开始

### 创建本地化文件

在项目根目录创建 `l.json`。

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

### 在项目中注册文件

```xml
<ItemGroup>
  <AdditionalFiles Include="l.json" />
  <None Update="l.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- `AdditionalFiles` 让源码生成器在编译期间读取该文件。
- `CopyToOutputDirectory` 适用于运行时也希望将 JSON 文件一并输出的场景。

### 使用生成代码

构建后，生成器会根据每个 JSON 键生成强类型成员。

```csharp
Console.WriteLine(L.Hello);
Console.WriteLine(L.SayHelloTo("World"));
```

- `hello` 会生成 `L.Hello`。
- `sayHelloTo` 会生成 `L.SayHelloTo(string name)`。

### 创建资源并解析文本

```csharp
using Senlinz.Localization;

var currentCulture = "zh";
var resolver = LStringResolver.Create(
    () => currentCulture,
    new EnResource(),
    new ZhResource());

Console.WriteLine(resolver[L.Hello]);
Console.WriteLine(resolver[L.SayHelloTo("世界")]);
```

- 常见场景下，直接把资源实例传给 `LStringResolver.Create(...)` 即可。
- 如果你只想直接使用 `l.json` 里的默认文本，可以调用 `LStringResolver.Create(() => currentCulture)`。

## 本地化文件规则

### 键格式

- JSON 键会被转换为生成的 C# 成员名。
- 请保持键名稳定，因为生成的 API 名称依赖这些键。
- 生成的成员名会尽量保持 JSON 原样，只把首字母变成大写以贴近 Pascal 风格，因此 `user_status` 会生成 `L.User_status`。
- 嵌套 JSON 对象会生成嵌套访问器，因此 `exception -> user -> notFound` 会生成 `L.Exception.User.NotFound(...)`。
- 这些嵌套路径在内部会使用点号连接的键，因此上面的示例会解析为 `exception.user.notFound`。
- 枚举键固定使用由枚举名和成员名组成的嵌套路径，因此 `UserType.Teacher` 会解析为 `userType.teacher`，对应 `L.UserType.Teacher`。

### 占位符参数

值中的占位符会生成方法参数。

```json
{
  "welcomeUser": "Welcome {userName}",
  "orderSummary": "Order {orderId} for {customerName}"
}
```

生成后的调用方式：

```csharp
var message1 = L.WelcomeUser("Alice");
var message2 = L.OrderSummary("SO-001", "Alice");
```

### 转义占位符

- 如果你希望保留花括号文本而不是生成参数，请在占位符名称前加 `$`。

```json
{
  "templateTip": "Use {$name} as a placeholder in your template."
}
```

- 生成后的默认文本会是 `Use {name} as a placeholder in your template.`

### 自定义文件名

如果你不想使用 `l.json`，可以在项目文件中设置 `SenlinzLocalizationFile`。

```xml
<PropertyGroup>
  <SenlinzLocalizationFile>localization.json</SenlinzLocalizationFile>
</PropertyGroup>

<ItemGroup>
  <AdditionalFiles Include="localization.json" />
  <None Update="localization.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

## 生成的类型

### `L`

- `L` 包含来自本地化 JSON 的所有强类型访问器。
- 普通文本会生成属性，带占位符的文本会生成方法。

### `LResource`

- `LResource` 是自动生成的抽象基类，每个本地化键都会对应一个受保护的抽象成员。
- 同时还会生成 `LDefaultResource`，用于提供 `l.json` 中的默认值。
- 通常为每种语言实现一个派生类并补全所有生成成员，这样 IDE 也更容易直接生成重写代码。

示例：

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
    protected override string SayHelloTo => "你好 {name}！";
    protected override string StatusReady => "就绪";
}

public sealed class FrResource : LResource
{
    public override string Culture => "fr";

    protected override string Hello => "Bonjour";
    protected override string SayHelloTo => "Bonjour {name} !";
    protected override string StatusReady => "Prêt";
}
```

### `LString`

- `LString` 保存本地化键、默认回退文本以及运行时参数。
- 通常你会从生成的 `L` 成员或枚举扩展方法中得到 `LString`。

## 解析本地化值

通过 `LStringResolver` 按当前语言解析文本。对于大多数场景，`LStringResolver.Create(...)` 是最简单的方式。

```csharp
using Senlinz.Localization;

var currentCulture = "zh";
var resolver = LStringResolver.Create(() => currentCulture, new EnResource(), new ZhResource());

Console.WriteLine(resolver[L.Hello]);
Console.WriteLine(resolver[L.SayHelloTo("世界")]);
```

如果你只想解析 `l.json` 默认值，可以直接使用生成的默认资源工厂：

```csharp
var resolver = LStringResolver.Create(() => currentCulture);
```

也可以使用扩展方法：

```csharp
var text = resolver.Resolve(L.Hello);
```

### 回退行为

- 如果当前语言没有对应资源，则会使用 `l.json` 中的默认文本。
- 如果资源存在但缺少某个键，同样会回退到默认文本。
- 资源字典会按解析器实例与语言维度分别缓存。

## 枚举本地化

### `[LString]`

给枚举加上 `[LString]` 后，会生成 `ToLString()` 扩展方法。

```csharp
[LString]
public enum UserType
{
    Teacher,
    Student
}
```

- 这会生成 `UserTypeExtensions.ToLString(this UserType value)`。
- 枚举值固定使用 `<枚举名CamelCase>.<成员名CamelCase>` 这种嵌套键模式。
- 例如 `UserType.Teacher` 生成的键就是 `userType.teacher`，对应访问器 `L.UserType.Teacher`。

对于上面的枚举，通常对应的本地化键是：

```json
{
  "userType": {
    "teacher": "Teacher",
    "student": "Student"
  }
}
```

### `[LStringKey]`

如果你希望只改枚举成员这一段的键名，请在成员上使用 `[LStringKey]`。

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

- `[LStringKey]` 只会替换最后一段枚举成员键名，枚举前缀段仍然始终由枚举名决定。

对应的 JSON：

```json
{
  "userType": {
    "teacher": "Teacher",
    "student": "Student"
  }
}
```

即使你传入点号路径或旧的完整键名，也只会取最后一段作为成员键名：

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

用法：

```csharp
var text = UserType.Student.ToLString();
Console.WriteLine(resolver[text]);
```

## 完整示例

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

### 枚举

```csharp
[LString]
public enum UserType
{
    Teacher,
    Student
}
```

### 资源与解析器

```csharp
using Senlinz.Localization;

var currentCulture = "zh";
var resolver = LStringResolver.Create(() => currentCulture, new ZhResource());

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

预期输出：

```text
你好
你好，世界！
学生
```

## 发布与文档站点

- 每次 push 和 pull request 都会触发校验工作流，执行 restore、build、test 与 pack，并上传生成的包制品。
- 创建并推送类似 `v2.0.0` 的版本标签即可触发 NuGet 发布工作流。
- 向 `main` 分支推送，或手动运行 Pages 工作流，即可把 `/docs` 中的静态站点发布到 GitHub Pages。

1. 先验证解决方案。
   ```bash
   dotnet test Senlinz.Localization.slnx --configuration Release
   ```
2. 如有需要，可在本地打包。
   ```bash
   dotnet pack Senlinz.Localization.slnx --configuration Release --output artifacts
   ```
3. 发布前应先确保 `Validate` GitHub Actions 工作流通过。
4. 本地与 CI 打包会产出 `.nupkg`，并在可用时产出 `.snupkg` 符号包制品、嵌入共享包图标，校验工作流也会上传这些制品供检查。
5. 标签构建成功后，`Publish NuGet packages` 工作流会先上传本次发布生成的制品，再将主包以及已生成的符号包一起发布到 NuGet。
6. `Deploy GitHub Pages` 工作流会发布 `/docs` 目录中的文档站点。
