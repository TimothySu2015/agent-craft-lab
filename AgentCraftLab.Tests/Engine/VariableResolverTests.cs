using AgentCraftLab.Engine.Models.Schema;
using AgentCraftLab.Engine.Services.Variables;

namespace AgentCraftLab.Tests.Engine;

/// <summary>
/// Phase F：改用 <see cref="VariableResolver"/> 實例 API + <see cref="VariableContext"/>。
/// 這些測試原本測 NodeReferenceResolver 靜態 API，F4 遷移後 NodeReferenceResolver
/// 整個刪除，測試改成 IVariableResolver 實例版。
/// </summary>
public class VariableResolverTests
{
    private static readonly IVariableResolver Resolver = new VariableResolver();

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

    private static VariableContext Ctx(
        IReadOnlyDictionary<string, string>? sys = null,
        IReadOnlyDictionary<string, string>? workflow = null,
        IReadOnlyDictionary<string, string>? env = null)
    {
        return new VariableContext
        {
            System = sys ?? new Dictionary<string, string>(),
            Workflow = workflow ?? new Dictionary<string, string>(),
            Environment = env ?? new Dictionary<string, string>(),
        };
    }

    [Fact]
    public void Resolve_SysVariable_Resolves()
    {
        var result = Resolver.Resolve("Hello {{sys:user_id}}", Ctx(sys: SysVars));
        Assert.Equal("Hello alice", result);
    }

    [Fact]
    public void Resolve_VarVariable_Resolves()
    {
        var result = Resolver.Resolve("Count is {{var:counter}}", Ctx(workflow: WorkflowVars));
        Assert.Equal("Count is 42", result);
    }

    [Fact]
    public void Resolve_EnvVariable_Resolves()
    {
        var result = Resolver.Resolve("Key: {{env:api_key}}", Ctx(env: EnvVars));
        Assert.Equal("Key: sk-123", result);
    }

    [Fact]
    public void Resolve_MixedPrefixes_ResolvesAll()
    {
        var text = "{{sys:user_id}} uses {{var:counter}} with {{env:api_url}}";
        var result = Resolver.Resolve(text, Ctx(SysVars, WorkflowVars, EnvVars));
        Assert.Equal("alice uses 42 with https://api.example.com", result);
    }

    [Fact]
    public void Resolve_UnknownVariable_PreservesMarker()
    {
        var result = Resolver.Resolve("{{var:missing}}", Ctx(workflow: WorkflowVars));
        Assert.Equal("{{var:missing}}", result);
    }

    [Fact]
    public void Resolve_NullText_ReturnsEmpty()
    {
        Assert.Equal("", Resolver.Resolve(null, Ctx(SysVars, WorkflowVars)));
    }

    [Fact]
    public void Resolve_EmptyText_ReturnsEmpty()
    {
        Assert.Equal("", Resolver.Resolve("", Ctx(SysVars, WorkflowVars)));
    }

    [Fact]
    public void Resolve_NoMarkers_ReturnsFastPath()
    {
        var result = Resolver.Resolve("plain text without markers", Ctx(SysVars, WorkflowVars));
        Assert.Equal("plain text without markers", result);
    }

    [Fact]
    public void Resolve_NodeReferenceWithoutNodeOutputs_PreservesMarker()
    {
        // {{node:X}} 需要 NodeOutputs — 沒提供時保留 marker
        var result = Resolver.Resolve("{{node:Agent-1}}", Ctx(SysVars, WorkflowVars));
        Assert.Equal("{{node:Agent-1}}", result);
    }

    [Fact]
    public void Resolve_CaseInsensitiveLookup()
    {
        var result = Resolver.Resolve("{{sys:User_Id}}", Ctx(sys: SysVars));
        Assert.Equal("alice", result);
    }

    [Fact]
    public void Resolve_EmptyDictionaries_DoesNotCrash()
    {
        var result = Resolver.Resolve("{{sys:x}} {{var:y}} {{env:z}}", VariableContext.Empty);
        Assert.Equal("{{sys:x}} {{var:y}} {{env:z}}", result);
    }

    [Fact]
    public void Resolve_WhitespaceTrimming()
    {
        var result = Resolver.Resolve("{{sys: user_id }}", Ctx(sys: SysVars));
        Assert.Equal("alice", result);
    }

    // ─── HasReferences ───

    [Theory]
    [InlineData("{{sys:x}}", true)]
    [InlineData("{{var:x}}", true)]
    [InlineData("{{env:x}}", true)]
    [InlineData("{{node:x}}", true)]   // HasReferences 含 node（Resolve 才需要 NodeOutputs）
    [InlineData("plain text", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void HasReferences_DetectsCorrectly(string? text, bool expected)
    {
        Assert.Equal(expected, Resolver.HasReferences(text));
    }

    // ─── ExtractReferences ───

    [Fact]
    public void ExtractReferences_ExtractsAllPrefixes()
    {
        var refs = Resolver.ExtractReferences("{{sys:user_id}} and {{var:counter}} and {{env:key}} and {{node:X}}");
        Assert.Equal(4, refs.Count);
        Assert.Contains(new VariableReference(VariableScope.System, "user_id"), refs);
        Assert.Contains(new VariableReference(VariableScope.Workflow, "counter"), refs);
        Assert.Contains(new VariableReference(VariableScope.Environment, "key"), refs);
        Assert.Contains(new VariableReference(VariableScope.NodeOutput, "X"), refs);
    }

    [Fact]
    public void ExtractReferences_EmptyText_ReturnsEmpty()
    {
        Assert.Empty(Resolver.ExtractReferences(null));
        Assert.Empty(Resolver.ExtractReferences(""));
    }

    // ─── Static helper — ExtractNodeReferenceNames ───

    [Fact]
    public void ExtractNodeReferenceNames_FiltersToNodeScopeOnly()
    {
        var names = VariableResolver.ExtractNodeReferenceNames(
            "{{sys:x}} {{node:Researcher}} {{var:y}} {{node:Writer}}");
        Assert.Equal(2, names.Count);
        Assert.Contains("Researcher", names);
        Assert.Contains("Writer", names);
    }

    [Fact]
    public void ExtractNodeReferenceNames_EmptyText_ReturnsEmpty()
    {
        Assert.Empty(VariableResolver.ExtractNodeReferenceNames(null));
        Assert.Empty(VariableResolver.ExtractNodeReferenceNames(""));
    }
}
