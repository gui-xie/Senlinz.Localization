using Senlinz.Localization;

namespace Senlinz.Localization.Tests;

[LString]
public enum SampleText
{
    [LStringKey("legacy.hello")]
    Hello,

    Ready
}

[LString]
public enum UserType
{
    Teacher,
    Student
}

public sealed class ZhResource : LResource
{
    public override string Culture => "zh";

    protected override string Hello => "你好";
}

public sealed class ZhFullResource : LResource
{
    private const string ExceptionUserNotFoundKey = "exception.user.notFound";
    private const string SampleTextHelloKey = "sampleText.hello";
    private const string SampleTextReadyKey = "sampleText.ready";
    private const string UserTypeTeacherKey = "userType.teacher";
    private const string UserTypeStudentKey = "userType.student";

    public override string Culture => "zh";

    protected override string Hello => "你好";

    protected override string SayHelloTo => "你好，{name}！";

    protected override string StatusReady => "就绪";

    protected override string QuotedMessage => "对 {name} 说“你好”！\n完成";

    public override Dictionary<string, string> GetResource()
    {
        var resource = base.GetResource();
        resource[ExceptionUserNotFoundKey] = "未找到用户 {userId}。";
        resource[SampleTextHelloKey] = "你好";
        resource[SampleTextReadyKey] = "就绪";
        resource[UserTypeTeacherKey] = "老师";
        resource[UserTypeStudentKey] = "学生";
        return resource;
    }
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
    public void Resolves_generated_localization_strings_with_resources()
    {
        var currentCulture = "zh";
        var resolver = new LStringResolver(() => currentCulture, new ZhFullResource());

        Assert.Equal("你好", resolver[L.Hello]);
        Assert.Equal("你好，世界！", resolver[L.SayHelloTo("世界")]);
        Assert.Equal("就绪", resolver[SampleText.Ready.ToLString()]);
    }

    [Fact]
    public void Falls_back_to_default_values_when_resource_is_missing()
    {
        var resolver = new LStringResolver(() => "en", new ZhFullResource());

        Assert.Equal("Hello", resolver[L.Hello]);
        Assert.Equal("Hello World!", resolver[L.SayHelloTo("World")]);
        Assert.Equal("Hello", resolver.Resolve(SampleText.Hello.ToLString()));
    }

    [Fact]
    public void Keeps_enum_prefix_for_lstringkey_mappings()
    {
        Assert.Equal("sampleText.hello", SampleText.Hello.ToLString().Key);
        Assert.Equal("sampleText.ready", SampleText.Ready.ToLString().Key);
    }

    [Fact]
    public void Maps_enum_values_to_matching_nested_localization_members()
    {
        var resolver = new LStringResolver(() => "zh", new ZhFullResource());

        Assert.Equal("userType.teacher", UserType.Teacher.ToLString().Key);
        Assert.Equal("Teacher", UserType.Teacher.ToLString().DefaultValue);
        Assert.Equal("老师", resolver[UserType.Teacher.ToLString()]);
        Assert.Equal("学生", resolver[UserType.Student.ToLString()]);
    }

    [Fact]
    public void Does_not_share_cached_resources_between_resolver_instances()
    {
        var firstResolver = new LStringResolver(() => "zh", new ZhFullResource());
        var secondResolver = new LStringResolver(() => "zh", new ZhAlternativeResource());

        Assert.Equal("你好", firstResolver[L.Hello]);
        Assert.Equal("您好", secondResolver[L.Hello]);
        Assert.Equal("你好，世界！", firstResolver[L.SayHelloTo("世界")]);
        Assert.Equal("您好，世界！", secondResolver[L.SayHelloTo("世界")]);
    }

    [Fact]
    public void Uses_generated_default_values_for_members_not_overridden_in_derived_resource()
    {
        var resolver = new LStringResolver(() => "zh", new ZhResource());

        Assert.Equal("你好", resolver[L.Hello]);
        Assert.Equal("Hello World!", resolver[L.SayHelloTo("World")]);
        Assert.Equal("Ready", resolver[L.StatusReady]);
    }

    [Fact]
    public void Resolves_escaped_json_content_from_generated_localizations()
    {
        var resolver = new LStringResolver(() => "en", new ZhFullResource());

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
        var resolver = new LStringResolver(() => "zh", new ZhFullResource());

        Assert.Equal("未找到用户 42。", resolver[L.Exception.User.NotFound("42")]);
    }

    [Fact]
    public void Falls_back_to_default_values_for_nested_json_objects()
    {
        var resolver = new LStringResolver(() => "en", new ZhFullResource());

        Assert.Equal("User '42' does not exist.", resolver[L.Exception.User.NotFound("42")]);
    }

    [Fact]
    public void Accepts_resource_provider_directly()
    {
        var provider = new LResourceProvider(new ZhFullResource());

        var resolver = new LStringResolver(() => "zh", provider);

        Assert.Equal("你好", resolver[L.Hello]);
        Assert.Equal("你好，世界！", resolver[L.SayHelloTo("世界")]);
    }
}
