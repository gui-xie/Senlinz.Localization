using Senlinz.Localization;

namespace Senlinz.Localization.Tests;

[LString]
public enum SampleText
{
    Hello,
    [LStringKey("statusReady")]
    Ready
}

public sealed class ZhResource : LResource
{
    public override string Culture => "zh";

    protected override string Hello => "你好";

    protected override string SayHelloTo => "你好，{name}！";

    protected override string StatusReady => "就绪";
}

public class LocalizationTests
{
    [Fact]
    public void Resolves_generated_localization_strings_with_resources()
    {
        var currentCulture = "zh";
        var resolver = CreateResolver(() => currentCulture);

        Assert.Equal("你好", resolver[L.Hello]);
        Assert.Equal("你好，世界！", resolver[L.SayHelloTo("世界")]);
        Assert.Equal("就绪", resolver[SampleText.Ready.ToLString()]);
    }

    [Fact]
    public void Falls_back_to_default_values_when_resource_is_missing()
    {
        var resolver = CreateResolver(() => "en");

        Assert.Equal("Hello", resolver[L.Hello]);
        Assert.Equal("Hello World!", resolver[L.SayHelloTo("World")]);
        Assert.Equal("Hello", resolver.Resolve(SampleText.Hello.ToLString()));
    }

    private static LStringResolver CreateResolver(GetCulture getCulture)
    {
        var provider = new LResourceProvider(new ZhResource());
        return new LStringResolver(getCulture, provider.GetResource);
    }
}
