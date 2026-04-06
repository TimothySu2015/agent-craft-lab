using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

public class EmailWhitelistTests
{
    [Fact]
    public void EmptyWhitelist_BlocksAll()
    {
        ToolImplementations.EmailWhitelist = [];
        Assert.False(InvokeIsEmailAllowed("anyone@example.com"));
    }

    [Fact]
    public void ExactMatch_Allowed()
    {
        ToolImplementations.EmailWhitelist = ["me@example.com"];
        Assert.True(InvokeIsEmailAllowed("me@example.com"));
    }

    [Fact]
    public void ExactMatch_CaseInsensitive()
    {
        ToolImplementations.EmailWhitelist = ["Me@Example.COM"];
        Assert.True(InvokeIsEmailAllowed("me@example.com"));
    }

    [Fact]
    public void WildcardDomain_Allowed()
    {
        ToolImplementations.EmailWhitelist = ["*@mycompany.com"];
        Assert.True(InvokeIsEmailAllowed("john@mycompany.com"));
        Assert.True(InvokeIsEmailAllowed("jane@mycompany.com"));
    }

    [Fact]
    public void WildcardDomain_OtherDomain_Blocked()
    {
        ToolImplementations.EmailWhitelist = ["*@mycompany.com"];
        Assert.False(InvokeIsEmailAllowed("hacker@evil.com"));
    }

    [Fact]
    public void NotInWhitelist_Blocked()
    {
        ToolImplementations.EmailWhitelist = ["me@example.com"];
        Assert.False(InvokeIsEmailAllowed("other@example.com"));
    }

    [Fact]
    public void MultiplePatterns_AnyMatch()
    {
        ToolImplementations.EmailWhitelist = ["me@example.com", "*@corp.com"];
        Assert.True(InvokeIsEmailAllowed("me@example.com"));
        Assert.True(InvokeIsEmailAllowed("anyone@corp.com"));
        Assert.False(InvokeIsEmailAllowed("other@gmail.com"));
    }

    // IsEmailAllowed 是 private，透過反射呼叫
    private static bool InvokeIsEmailAllowed(string email)
    {
        var method = typeof(ToolImplementations).GetMethod("IsEmailAllowed",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (bool)method!.Invoke(null, [email])!;
    }
}
