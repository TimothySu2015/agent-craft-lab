using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

public class VariableResolverTests
{
    private static readonly Dictionary<string, string> SysVars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["user_id"] = "alice",
        ["timestamp"] = "2026-04-11T12:00:00Z",
        ["execution_id"] = "exec-123",
    };

    private static readonly Dictionary<string, string> WorkflowVars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["counter"] = "42",
        ["customer_name"] = "王大明",
    };

    private static readonly Dictionary<string, string> EnvVars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["api_key"] = "sk-123",
        ["api_url"] = "https://api.example.com",
    };

    [Fact]
    public void ResolveVariables_SysVariable_Resolves()
    {
        var result = NodeReferenceResolver.ResolveVariables("Hello {{sys:user_id}}", SysVars, null);
        Assert.Equal("Hello alice", result);
    }

    [Fact]
    public void ResolveVariables_VarVariable_Resolves()
    {
        var result = NodeReferenceResolver.ResolveVariables("Count is {{var:counter}}", null, WorkflowVars);
        Assert.Equal("Count is 42", result);
    }

    [Fact]
    public void ResolveVariables_EnvVariable_Resolves()
    {
        var result = NodeReferenceResolver.ResolveVariables("Key: {{env:api_key}}", null, null, EnvVars);
        Assert.Equal("Key: sk-123", result);
    }

    [Fact]
    public void ResolveVariables_MixedPrefixes_ResolvesAll()
    {
        var text = "{{sys:user_id}} uses {{var:counter}} with {{env:api_url}}";
        var result = NodeReferenceResolver.ResolveVariables(text, SysVars, WorkflowVars, EnvVars);
        Assert.Equal("alice uses 42 with https://api.example.com", result);
    }

    [Fact]
    public void ResolveVariables_UnknownVariable_PreservesMarker()
    {
        var result = NodeReferenceResolver.ResolveVariables("{{var:missing}}", null, WorkflowVars);
        Assert.Equal("{{var:missing}}", result);
    }

    [Fact]
    public void ResolveVariables_NullText_ReturnsEmpty()
    {
        Assert.Equal("", NodeReferenceResolver.ResolveVariables(null, SysVars, WorkflowVars));
    }

    [Fact]
    public void ResolveVariables_EmptyText_ReturnsEmpty()
    {
        Assert.Equal("", NodeReferenceResolver.ResolveVariables("", SysVars, WorkflowVars));
    }

    [Fact]
    public void ResolveVariables_NoMarkers_ReturnsFastPath()
    {
        var result = NodeReferenceResolver.ResolveVariables("plain text without markers", SysVars, WorkflowVars);
        Assert.Equal("plain text without markers", result);
    }

    [Fact]
    public void ResolveVariables_NodeReference_NotResolved()
    {
        var result = NodeReferenceResolver.ResolveVariables("{{node:Agent-1}}", SysVars, WorkflowVars);
        Assert.Equal("{{node:Agent-1}}", result);
    }

    [Fact]
    public void ResolveVariables_CaseInsensitiveLookup()
    {
        var result = NodeReferenceResolver.ResolveVariables("{{sys:User_Id}}", SysVars, null);
        Assert.Equal("alice", result);
    }

    [Fact]
    public void ResolveVariables_NullDictionaries_DoesNotCrash()
    {
        var result = NodeReferenceResolver.ResolveVariables("{{sys:x}} {{var:y}} {{env:z}}", null, null, null);
        Assert.Equal("{{sys:x}} {{var:y}} {{env:z}}", result);
    }

    [Fact]
    public void ResolveVariables_WhitespaceTrimming()
    {
        var result = NodeReferenceResolver.ResolveVariables("{{sys: user_id }}", SysVars, null);
        Assert.Equal("alice", result);
    }

    // ─── HasVariableReferences ───

    [Theory]
    [InlineData("{{sys:x}}", true)]
    [InlineData("{{var:x}}", true)]
    [InlineData("{{env:x}}", true)]
    [InlineData("{{node:x}}", false)]
    [InlineData("plain text", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void HasVariableReferences_DetectsCorrectly(string? text, bool expected)
    {
        Assert.Equal(expected, NodeReferenceResolver.HasVariableReferences(text));
    }

    // ─── ExtractVariableNames ───

    [Fact]
    public void ExtractVariableNames_ExtractsAllPrefixes()
    {
        var names = NodeReferenceResolver.ExtractVariableNames("{{sys:user_id}} and {{var:counter}} and {{env:key}}");
        Assert.Equal(3, names.Count);
        Assert.Contains(("sys", "user_id"), names);
        Assert.Contains(("var", "counter"), names);
        Assert.Contains(("env", "key"), names);
    }

    [Fact]
    public void ExtractVariableNames_EmptyText_ReturnsEmpty()
    {
        Assert.Empty(NodeReferenceResolver.ExtractVariableNames(null));
        Assert.Empty(NodeReferenceResolver.ExtractVariableNames(""));
    }
}
