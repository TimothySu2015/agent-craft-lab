using AgentCraftLab.Engine.Models.Schema;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Services.Variables;

namespace AgentCraftLab.Tests.Engine.Schema;

/// <summary>
/// 驗證 VariableResolver — 統一解析 {{sys:}} / {{var:}} / {{runtime:}} / {{env:}} / {{node:}} 的 single entry point。
/// </summary>
public class VariableResolverTests
{
    private readonly VariableResolver _resolver = new();

    private static VariableContext BuildContext() => new()
    {
        System = new Dictionary<string, string> { ["runId"] = "r-42", ["userId"] = "u-1" },
        Workflow = new Dictionary<string, string> { ["topic"] = "Claude", ["lang"] = "zh-TW" },
        Runtime = new Dictionary<string, string> { ["overrideTopic"] = "Override" },
        Environment = new Dictionary<string, string> { ["API_TOKEN"] = "tok-xxx" },
        NodeOutputs = new Dictionary<string, string> { ["Researcher"] = "Research output" }
    };

    [Fact]
    public void Resolve_SystemVariable()
    {
        var result = _resolver.Resolve("Run: {{sys:runId}}", BuildContext());
        Assert.Equal("Run: r-42", result);
    }

    [Fact]
    public void Resolve_WorkflowVariable()
    {
        var result = _resolver.Resolve("Topic: {{var:topic}}", BuildContext());
        Assert.Equal("Topic: Claude", result);
    }

    [Fact]
    public void Resolve_RuntimeVariable()
    {
        var result = _resolver.Resolve("Override: {{runtime:overrideTopic}}", BuildContext());
        Assert.Equal("Override: Override", result);
    }

    [Fact]
    public void Resolve_EnvironmentVariable()
    {
        var result = _resolver.Resolve("Bearer {{env:API_TOKEN}}", BuildContext());
        Assert.Equal("Bearer tok-xxx", result);
    }

    [Fact]
    public void Resolve_NodeOutputByName()
    {
        var result = _resolver.Resolve("Summary: {{node:Researcher}}", BuildContext());
        Assert.Equal("Summary: Research output", result);
    }

    [Fact]
    public void Resolve_NodeOutputByNameMap()
    {
        var ctx = new VariableContext
        {
            NodeOutputs = new Dictionary<string, string> { ["node-1"] = "via id lookup" },
            NodeNameMap = new Dictionary<string, string> { ["Researcher"] = "node-1" }
        };

        var result = _resolver.Resolve("{{node:Researcher}}", ctx);
        Assert.Equal("via id lookup", result);
    }

    [Fact]
    public void Resolve_MultipleScopesInOneString()
    {
        var input = "User {{sys:userId}} asked about {{var:topic}}, calling {{env:API_TOKEN}}";
        var result = _resolver.Resolve(input, BuildContext());
        Assert.Equal("User u-1 asked about Claude, calling tok-xxx", result);
    }

    [Fact]
    public void Resolve_UnknownReference_LeavesUntouched()
    {
        var result = _resolver.Resolve("{{var:missing}}", BuildContext());
        Assert.Equal("{{var:missing}}", result);
    }

    [Fact]
    public void Resolve_EmptyText_ReturnsEmpty()
    {
        Assert.Equal("", _resolver.Resolve(null, VariableContext.Empty));
        Assert.Equal("", _resolver.Resolve("", VariableContext.Empty));
    }

    [Fact]
    public void Resolve_NoReferences_IsNoOp()
    {
        var text = "Plain text no references";
        Assert.Equal(text, _resolver.Resolve(text, VariableContext.Empty));
    }

    [Fact]
    public void HasReferences_DetectsAllPrefixes()
    {
        Assert.True(_resolver.HasReferences("{{sys:x}}"));
        Assert.True(_resolver.HasReferences("{{var:x}}"));
        Assert.True(_resolver.HasReferences("{{runtime:x}}"));
        Assert.True(_resolver.HasReferences("{{env:x}}"));
        Assert.True(_resolver.HasReferences("{{node:x}}"));
        Assert.False(_resolver.HasReferences("plain text"));
        Assert.False(_resolver.HasReferences("{{unknown:x}}"));
    }

    [Fact]
    public void ExtractReferences_ReturnsDistinctTuples()
    {
        var refs = _resolver.ExtractReferences(
            "{{var:a}} {{var:a}} {{sys:b}} {{node:c}}");

        Assert.Equal(3, refs.Count);
        Assert.Contains(refs, r => r.Name == "a" && r.Scope == VariableScope.Workflow);
        Assert.Contains(refs, r => r.Name == "b" && r.Scope == VariableScope.System);
        Assert.Contains(refs, r => r.Name == "c" && r.Scope == VariableScope.NodeOutput);
    }

    [Fact]
    public async Task ResolveAsync_LargeNodeOutput_IsCompressed()
    {
        var longOutput = new string('x', 3000); // > CompressionThreshold (2000)
        var ctx = new VariableContext
        {
            NodeOutputs = new Dictionary<string, string> { ["Big"] = longOutput }
        };

        var fakeCompactor = new FakeContextCompactor("<COMPRESSED>");
        var result = await _resolver.ResolveAsync(
            "{{node:Big}}", ctx, fakeCompactor, "context", CancellationToken.None);

        Assert.Equal("<COMPRESSED>", result);
        Assert.Equal(1, fakeCompactor.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_SmallNodeOutput_NotCompressed()
    {
        var ctx = new VariableContext
        {
            NodeOutputs = new Dictionary<string, string> { ["Small"] = "short" }
        };

        var fakeCompactor = new FakeContextCompactor("<SHOULD_NOT_BE_USED>");
        var result = await _resolver.ResolveAsync(
            "{{node:Small}}", ctx, fakeCompactor, "context", CancellationToken.None);

        Assert.Equal("short", result);
        Assert.Equal(0, fakeCompactor.CallCount);
    }

    private sealed class FakeContextCompactor(string returnValue) : IContextCompactor
    {
        public int CallCount { get; private set; }

        public Task<string?> CompressAsync(
            string text,
            string context,
            int tokenBudget,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<string?>(returnValue);
        }
    }
}
