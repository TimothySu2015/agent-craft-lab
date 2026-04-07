using AgentCraftLab.Script;

namespace AgentCraftLab.Tests.Script;

public class ScriptEngineFactoryTests
{
    [Fact]
    public void GetEngine_JavaScript_ReturnsJint()
    {
        var factory = new ScriptEngineFactory()
            .Register("javascript", new JintScriptEngine())
            .Register("csharp", new RoslynScriptEngine());

        var engine = factory.GetEngine("javascript");
        Assert.IsType<JintScriptEngine>(engine);
    }

    [Fact]
    public void GetEngine_CSharp_ReturnsRoslyn()
    {
        var factory = new ScriptEngineFactory()
            .Register("javascript", new JintScriptEngine())
            .Register("csharp", new RoslynScriptEngine());

        var engine = factory.GetEngine("csharp");
        Assert.IsType<RoslynScriptEngine>(engine);
    }

    [Fact]
    public void GetEngine_JsAlias_ReturnsJint()
    {
        var factory = new ScriptEngineFactory()
            .Register("javascript", new JintScriptEngine());

        var engine = factory.GetEngine("js");
        Assert.IsType<JintScriptEngine>(engine);
    }

    [Fact]
    public void GetEngine_CSharpAliases_ReturnRoslyn()
    {
        var factory = new ScriptEngineFactory()
            .Register("csharp", new RoslynScriptEngine());

        Assert.IsType<RoslynScriptEngine>(factory.GetEngine("c#"));
        Assert.IsType<RoslynScriptEngine>(factory.GetEngine("cs"));
        Assert.IsType<RoslynScriptEngine>(factory.GetEngine("dotnet"));
    }

    [Fact]
    public void GetEngine_NullOrEmpty_DefaultsToJavaScript()
    {
        var factory = new ScriptEngineFactory()
            .Register("javascript", new JintScriptEngine());

        Assert.IsType<JintScriptEngine>(factory.GetEngine(""));
        Assert.IsType<JintScriptEngine>(factory.GetEngine(null!));
    }

    [Fact]
    public void GetEngine_Unsupported_Throws()
    {
        var factory = new ScriptEngineFactory()
            .Register("javascript", new JintScriptEngine());

        var ex = Assert.Throws<NotSupportedException>(() => factory.GetEngine("python"));
        Assert.Contains("python", ex.Message);
        Assert.Contains("javascript", ex.Message);
    }

    [Fact]
    public void SupportedLanguages_ReturnsRegistered()
    {
        var factory = new ScriptEngineFactory()
            .Register("javascript", new JintScriptEngine())
            .Register("csharp", new RoslynScriptEngine());

        Assert.Contains("javascript", factory.SupportedLanguages);
        Assert.Contains("csharp", factory.SupportedLanguages);
        Assert.Equal(2, factory.SupportedLanguages.Count);
    }

    [Fact]
    public void GetEngine_CaseInsensitive()
    {
        var factory = new ScriptEngineFactory()
            .Register("javascript", new JintScriptEngine())
            .Register("csharp", new RoslynScriptEngine());

        Assert.IsType<JintScriptEngine>(factory.GetEngine("JavaScript"));
        Assert.IsType<RoslynScriptEngine>(factory.GetEngine("CSharp"));
        Assert.IsType<RoslynScriptEngine>(factory.GetEngine("CSHARP"));
    }
}
