# Senlinz.Localization

**中文** | [English](../README.md)

一个面向 .NET 的基于 JSON 的本地化源码生成器，用于生成强类型本地化访问器、资源基类以及枚举本地化辅助方法。

编译时要求：需要 .NET 8 SDK 或更高版本（源码生成器依赖 Roslyn 4.8）。

运行时兼容性：生成出的运行时支持代码目标框架为 `netstandard2.0`，因此可运行在 .NET Framework 4.6.1+、.NET Core 2.0+ 及更高版本运行时上。

说明：改用更传统的 C# 语法，主要降低的是编译器和工具链门槛；真正决定运行时兼容性的仍然是 `netstandard2.0` 目标框架。

- 文档站点：<https://gui-xie.github.io/Senlinz.Localization/>
- 当前已发布包版本：`3.3.0`

## 快速导航

- [功能特性](#功能特性)
- [快速开始](#快速开始)
- [生成的类型](#生成的类型)
- [解析本地化值](#解析本地化值)
- [枚举本地化](#枚举本地化)
- [完整示例](#完整示例)

## 功能特性

- 从主语言 JSON 文件生成 `L` 访问器。
- 生成 `LResource` 基类，并为每个发现的语言 JSON 自动生成具体资源类。
- 通过 `LString`、`LStringResolver` 和生成的 `LResource` 类型解析本地化文本。
- 通过 `[LString]` 与 `[LStringKey]` 将枚举值转换为本地化键。
- `Senlinz.Localization` 与 `Senlinz.Localization.Abstractions` 可分别作为独立的 NuGet 包发布。

## 包选择

### `Senlinz.Localization`

- 如果你的项目需要从 JSON 自动生成本地化代码，请安装这个包。

```bash
dotnet add package Senlinz.Localization
```

### `Senlinz.Localization.Abstractions`

- 如果你只需要共享的本地化契约，而不需要源码生成器，请安装这个包。

```bash
dotnet add package Senlinz.Localization.Abstractions
```

## 快速开始

### 创建本地化文件

默认把本地化 JSON 放到 `L/` 文件夹下。除非你用 `SenlinzLocalizationFile` 覆盖，默认主文件是 `en.json`。

示例目录：

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

### 把文件放到本地化目录里

```xml
<ItemGroup>
  <AdditionalFiles Include="L/**/*.json" />
</ItemGroup>
```

- 包默认会把 `$(SenlinzLocalizationFolder)/**/*.json` 自动加入 `AdditionalFiles`，所以只要文件放在 `L/` 目录下，一般不需要额外项目配置。
- 上面的 `AdditionalFiles` 配置只在你想覆盖或扩展默认包含规则时才需要。
- 生成器仍然只会读取配置的本地化目录（含子目录）下的 JSON 文件。

### 使用生成代码

构建后，生成器会根据每个 JSON 键生成强类型成员。

```csharp
Console.WriteLine(L.Hello);
Console.WriteLine(L.SayHelloTo("World"));
```

- `hello` 会生成 `L.Hello`。
- `sayHelloTo` 会生成 `L.SayHelloTo(string name)`。

### 使用生成的语言资源并解析文本

```csharp
using Senlinz.Localization;

var currentCulture = "zh";
var resolver = new LStringResolver(() => currentCulture);

Console.WriteLine(resolver[L.Hello]);
Console.WriteLine(resolver[L.SayHelloTo("世界")]);
```

- 常见场景下，`new LStringResolver(() => currentCulture)` 会直接使用生成的解析器并自动包含所有发现的资源。
- 如果你需要运行时覆盖，可以改用接收显式资源参数的重载并传入自定义 `LResource`。

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

### 主本地化文件

`SenlinzLocalizationFile` 用来指定哪个 JSON 文件负责生成 `L`，同时它对应的生成资源类也会作为默认资源。默认值是 `en.json`。

```xml
<PropertyGroup>
  <SenlinzLocalizationFolder>L</SenlinzLocalizationFolder>
  <SenlinzLocalizationFile>zh.json</SenlinzLocalizationFile>
</PropertyGroup>

<ItemGroup>
  <AdditionalFiles Include="L/**/*.json" />
</ItemGroup>
```

### 本地化文件夹

`SenlinzLocalizationFolder` 用来指定生成器扫描本地化 JSON 的目录。默认值是 `L`，并且会递归包含所有子目录。

```xml
<PropertyGroup>
  <SenlinzLocalizationFolder>Localization</SenlinzLocalizationFolder>
  <SenlinzLocalizationFile>en.json</SenlinzLocalizationFile>
</PropertyGroup>

<ItemGroup>
  <AdditionalFiles Include="**/*.json" />
</ItemGroup>
```

- 只有位于配置目录下的 JSON 文件才会被当成本地化输入。
- 这样即使 `AdditionalFiles` 里还有其他用途的 JSON，生成器也不会把它们误判为本地化资源。

## 生成的类型

### `L`

- `L` 包含来自本地化 JSON 的所有强类型访问器。
- 普通文本会生成属性，带占位符的文本会生成方法。

### `LResource`

- `LResource` 是自动生成的抽象基类，它的 `GetResource()` 会返回主本地化文件对应的默认字典。
- 生成器还会为每个发现的语言 JSON 自动生成一个具体的 internal `*Resource` 类，例如 `EnResource`、`ZhResource`。
- `new LStringResolver(() => currentCulture)` 会直接使用生成的解析器并自动接入所有发现的资源。
- 如果你需要运行时覆盖，仍然可以继续手写派生自 `LResource` 的自定义资源类，并重写 `GetResource()`。

### `LString`

- `LString` 保存本地化键、默认回退文本以及运行时参数。
- 通常你会从生成的 `L` 成员或枚举扩展方法中得到 `LString`。

## 解析本地化值

通过 `LStringResolver` 按当前语言解析文本。对于大多数场景，`new LStringResolver(...)` 是最简单的方式。

```csharp
using Senlinz.Localization;

var currentCulture = "zh";
var resolver = new LStringResolver(() => currentCulture);

Console.WriteLine(resolver[L.Hello]);
Console.WriteLine(resolver[L.SayHelloTo("世界")]);
```

如果你需要运行时覆盖，可以继承 `LResource`、重写 `GetResource()`，再通过显式资源重载传入自定义资源。

也可以直接调用实例方法：

```csharp
var text = resolver.Resolve(L.Hello);
```

### 回退行为

- 如果当前语言没有对应资源，则会使用主 JSON 文件中的默认文本。
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

### 枚举

```csharp
[LString]
public enum UserType
{
    Teacher,
    Student
}
```

### 解析器

```csharp
using Senlinz.Localization;

var currentCulture = "zh";
var resolver = new LStringResolver(() => currentCulture);

Console.WriteLine(resolver[L.Hello]);
Console.WriteLine(resolver[L.SayHelloTo("世界")]);
Console.WriteLine(resolver[UserType.Student.ToLString()]);
```

预期输出：

```text
你好
你好，世界！
学生
```

## 发布与文档站点

- 当前已经发布到 NuGet 的版本是 `3.3.0`。
- 请保持 `README.md`、`README.zh-CN.md`、`docs/README.md` 和 `docs/zh-CN/README.md` 同步，确保仓库首页与 Docsify 文档站点展示一致的发布状态。
- 在创建下一次发布标签前，把面向包使用者的重要变更补充到 `CHANGELOG.md` 与 `RELEASE_NOTES.md`。
- 推送 `v*` 或 `V*` 标签会触发 NuGet 发布工作流，而 `docs/` 目录中的内容会通过文档工作流部署到站点。
