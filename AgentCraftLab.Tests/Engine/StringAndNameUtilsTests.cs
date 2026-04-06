using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

public class StringAndNameUtilsTests
{
    [Fact]
    public void StringUtils_Truncate_Short_NoChange()
    {
        Assert.Equal("hi", StringUtils.Truncate("hi", 10));
    }

    [Fact]
    public void StringUtils_Truncate_Long_CutsWithSuffix()
    {
        var result = StringUtils.Truncate("1234567890", 5);
        Assert.Equal("12345...", result);
    }

    [Fact]
    public void StringUtils_Truncate_CustomSuffix()
    {
        var result = StringUtils.Truncate("abcdef", 3, "~~");
        Assert.Equal("abc~~", result);
    }

    [Fact]
    public void NameUtils_Sanitize_LowercaseAndReplace()
    {
        Assert.Equal("hello_world", NameUtils.Sanitize("Hello World"));
    }

    [Fact]
    public void NameUtils_Sanitize_SpecialChars()
    {
        Assert.Equal("test_123", NameUtils.Sanitize("Test@123!"));
    }

    [Fact]
    public void NameUtils_Sanitize_AlreadyClean()
    {
        Assert.Equal("clean", NameUtils.Sanitize("clean"));
    }
}
