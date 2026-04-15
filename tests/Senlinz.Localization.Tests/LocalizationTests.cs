using Senlinz.Localization;

namespace Senlinz.Localization.Tests;

[LString]
public enum SampleText
{
    [LStringKey("SampleText_Hello")]
    Hello,

    [LStringKey("sampleText_ready")]
    Ready
}

[LString]
public enum UserType
{
    Teacher,
    Student
}

public sealed class PartialEnResource : LResource
{
    public override string Culture => "en";

    protected override string Hello => "Hello";

    protected override string SayHelloTo => string.Empty;

    protected override string StatusReady => string.Empty;

    protected override string QuotedMessage => string.Empty;
}

public sealed class ZhAlternativeResource : LResource
{
    private const string ExceptionUserNotFoundKey = "exception.user.notFound";
    private const string SampleTextHelloKey = "sampleText.hello";
    private const string SampleTextReadyKey = "sampleText.ready";
    private const string UserTypeTeacherKey = "userType.teacher";
    private const string UserTypeStudentKey = "userType.student";

    public override string Culture => "zh";

    protected override string Hello => "您好";

    protected override string SayHelloTo => "您好，{name}！";

    protected override string StatusReady => "已就绪";

    protected override string QuotedMessage => "向 {name} 问好！\n已完成";

    public override Dictionary<string, string> GetResource()
    {
        var resource = base.GetResource();
        resource[ExceptionUserNotFoundKey] = "找不到 ID 为 {userId} 的用户。";
        resource[SampleTextHelloKey] = "您好";
        resource[SampleTextReadyKey] = "已就绪";
        resource[UserTypeTeacherKey] = "讲师";
        resource[UserTypeStudentKey] = "学员";
        return resource;
    }
}

public class LocalizationTests
{
    [Fact]
    public void Generates_resource_classes_for_all_culture_json_files()
    {
        var en = new EnResource();
        var zh = new ZhResource();

        Assert.False(typeof(EnResource).IsPublic);
        Assert.False(typeof(ZhResource).IsPublic);
        Assert.Equal("en", en.Culture);
        Assert.Equal("Hello", en.GetResource()["hello"]);
        Assert.Equal("User '42' does not exist.", en.GetResource()["exception.user.notFound"].Replace("{userId}", "42"));

        Assert.Equal("zh", zh.Culture);
        Assert.Equal("你好", zh.GetResource()["hello"]);
        Assert.Equal("未找到用户 42。", zh.GetResource()["exception.user.notFound"].Replace("{userId}", "42"));
        Assert.IsAssignableFrom<IDefaultLResource>(en);
        Assert.False(typeof(IDefaultLResource).IsAssignableFrom(typeof(ZhResource)));
    }

    [Fact]
    public void Resolves_generated_localization_strings_with_resources()
    {
        var currentCulture = "en";
        var resolver = LStringResolver.Create(() => currentCulture);

        Assert.Equal("Hello", resolver[L.Hello]);
        Assert.Equal("Hello World!", resolver[L.SayHelloTo("World")]);
        Assert.Equal("Ready", resolver[SampleText.Ready.ToLString()]);
    }

    [Fact]
    public void Falls_back_to_primary_values_when_resource_is_missing()
    {
        var resolver = LStringResolver.Create(() => "fr");

        Assert.Equal("Hello", resolver[L.Hello]);
        Assert.Equal("Hello World!", resolver[L.SayHelloTo("World")]);
        Assert.Equal("Hello", resolver.Resolve(SampleText.Hello.ToLString()));
    }

    [Fact]
    public void Applies_lstringkey_override_to_member_segment()
    {
        Assert.Equal("sampleText.hello", SampleText.Hello.ToLString().Key);
        Assert.Equal("sampleText.ready", SampleText.Ready.ToLString().Key);
    }

    [Fact]
    public void Maps_enum_values_to_matching_nested_localization_members()
    {
        var resolver = LStringResolver.Create(() => "en");

        Assert.Equal("userType.teacher", UserType.Teacher.ToLString().Key);
        Assert.Equal("Teacher", UserType.Teacher.ToLString().DefaultValue);
        Assert.Equal("Teacher", resolver[UserType.Teacher.ToLString()]);
        Assert.Equal("Student", resolver[UserType.Student.ToLString()]);
    }

    [Fact]
    public void Does_not_share_cached_resources_between_resolver_instances()
    {
        var firstResolver = LStringResolver.Create(() => "zh", new ZhResource());
        var secondResolver = LStringResolver.Create(() => "zh", new ZhAlternativeResource());

        Assert.Equal("你好", firstResolver[L.Hello]);
        Assert.Equal("您好", secondResolver[L.Hello]);
        Assert.Equal("你好，世界！", firstResolver[L.SayHelloTo("世界")]);
        Assert.Equal("您好，世界！", secondResolver[L.SayHelloTo("世界")]);
        Assert.Equal("未找到用户 42。", firstResolver[L.Exception.User.NotFound("42")]);
        Assert.Equal("找不到 ID 为 42 的用户。", secondResolver[L.Exception.User.NotFound("42")]);
    }

    [Fact]
    public void Uses_generated_primary_values_for_members_not_overridden_in_derived_resource()
    {
        var resolver = LStringResolver.Create(() => "en", new PartialEnResource());

        Assert.Equal("Hello", resolver[L.Hello]);
        Assert.Equal("Hello World!", resolver[L.SayHelloTo("World")]);
        Assert.Equal("Ready", resolver[L.StatusReady]);
    }

    [Fact]
    public void Resolves_escaped_json_content_from_generated_localizations()
    {
        var resolver = LStringResolver.Create(() => "en", new EnResource());

        Assert.Equal("Say \"Hello\" to Alice!\nDone", resolver[L.QuotedMessage("Alice")]);
    }

    [Fact]
    public void Uses_dotted_keys_for_nested_json_objects()
    {
        Assert.Equal("exception.user.notFound", L.Exception.User.NotFound("42").Key);
    }

    [Fact]
    public void Generates_nested_api_for_nested_json_objects()
    {
        var resolver = LStringResolver.Create(() => "en", new EnResource());

        Assert.Equal("User '42' does not exist.", resolver[L.Exception.User.NotFound("42")]);
    }

    [Fact]
    public void Falls_back_to_primary_values_for_nested_json_objects()
    {
        var resolver = LStringResolver.Create(() => "fr");

        Assert.Equal("User '42' does not exist.", resolver[L.Exception.User.NotFound("42")]);
    }

    [Fact]
    public void Creates_resolver_from_resource_array()
    {
        var currentCulture = "zh";
        var resolver = LStringResolver.Create(() => currentCulture, new EnResource(), new ZhResource());

        Assert.Equal("你好", resolver[L.Hello]);
        Assert.Equal("你好，世界！", resolver[L.SayHelloTo("世界")]);

        currentCulture = "en";
        Assert.Equal("Hello", resolver[L.Hello]);
        Assert.Equal("Hello Alice!", resolver[L.SayHelloTo("Alice")]);
    }

    [Fact]
    public void Creates_default_resolver_from_generated_resources()
    {
        var currentCulture = "zh";
        var resolver = LStringResolver.Create(() => currentCulture);

        Assert.Equal("你好", resolver[L.Hello]);
        Assert.Equal("你好，Alice！", resolver[L.SayHelloTo("Alice")]);
        Assert.Equal("未找到用户 42。", resolver[L.Exception.User.NotFound("42")]);

        currentCulture = "fr";

        Assert.Equal("Hello", resolver[L.Hello]);
        Assert.Equal("Hello Alice!", resolver[L.SayHelloTo("Alice")]);
        Assert.Equal("User '42' does not exist.", resolver[L.Exception.User.NotFound("42")]);
    }

    [Fact]
    public void Creates_default_resolver_from_generated_resources_in_specified_assembly()
    {
        var currentCulture = "zh";
        var resolver = LStringResolver.Create(() => currentCulture, typeof(LocalizationTests).Assembly);

        Assert.Equal("你好", resolver[L.Hello]);

        currentCulture = "en";
        Assert.Equal("Hello", resolver[L.Hello]);
    }
}
