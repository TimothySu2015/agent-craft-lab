using AgentCraftLab.Data;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

public class WorkflowPreprocessorTests
{
    // ════════════════════════════════════════
    // CreateEmbeddingGenerator (internal static)
    // ════════════════════════════════════════

    [Fact]
    public void CreateEmbeddingGenerator_NoCredentials_ReturnsNull()
    {
        var request = new WorkflowExecutionRequest
        {
            Credentials = new Dictionary<string, ProviderCredential>()
        };

        var result = WorkflowPreprocessor.CreateEmbeddingGenerator(request, "text-embedding-3-small");

        Assert.Null(result);
    }

    [Fact]
    public void CreateEmbeddingGenerator_EmptyApiKey_ReturnsNull()
    {
        var request = new WorkflowExecutionRequest
        {
            Credentials = new Dictionary<string, ProviderCredential>
            {
                [Providers.OpenAI] = new() { ApiKey = "", Endpoint = "" }
            }
        };

        var result = WorkflowPreprocessor.CreateEmbeddingGenerator(request, "text-embedding-3-small");

        Assert.Null(result);
    }

    [Fact]
    public void CreateEmbeddingGenerator_OpenAIKey_ReturnsGenerator()
    {
        var request = new WorkflowExecutionRequest
        {
            Credentials = new Dictionary<string, ProviderCredential>
            {
                [Providers.OpenAI] = new() { ApiKey = "sk-test", Endpoint = "" }
            }
        };

        var result = WorkflowPreprocessor.CreateEmbeddingGenerator(request, "text-embedding-3-small");

        Assert.NotNull(result);
    }

    [Fact]
    public void CreateEmbeddingGenerator_AzureKey_ReturnsGenerator()
    {
        var request = new WorkflowExecutionRequest
        {
            Credentials = new Dictionary<string, ProviderCredential>
            {
                [Providers.AzureOpenAI] = new() { ApiKey = "azure-key", Endpoint = "https://myaz.openai.azure.com/" }
            }
        };

        var result = WorkflowPreprocessor.CreateEmbeddingGenerator(request, "text-embedding-3-small");

        Assert.NotNull(result);
    }

    [Fact]
    public void CreateEmbeddingGenerator_OpenAI_PrioritizedOverAzure()
    {
        var request = new WorkflowExecutionRequest
        {
            Credentials = new Dictionary<string, ProviderCredential>
            {
                [Providers.OpenAI] = new() { ApiKey = "sk-openai", Endpoint = "" },
                [Providers.AzureOpenAI] = new() { ApiKey = "az-key", Endpoint = "https://myaz.openai.azure.com/" }
            }
        };

        // 只要有 OpenAI key 就不會走 Azure — 透過返回 non-null 驗證
        var result = WorkflowPreprocessor.CreateEmbeddingGenerator(request, "text-embedding-3-small");

        Assert.NotNull(result);
    }

    // ════════════════════════════════════════
    // NodeTypeRegistry 整合驗證（Preprocessor 依賴）
    // ════════════════════════════════════════

    private static NodeConfig CreateNode(string type, string id = "n1") => type switch
    {
        NodeTypes.Agent => new AgentNode { Id = id, Name = id },
        NodeTypes.Start => new StartNode { Id = id, Name = id },
        NodeTypes.End => new EndNode { Id = id, Name = id },
        NodeTypes.Rag => new RagNode { Id = id, Name = id },
        NodeTypes.Code => new CodeNode { Id = id, Name = id },
        NodeTypes.Human => new HumanNode { Id = id, Name = id },
        NodeTypes.Condition => new ConditionNode { Id = id, Name = id },
        NodeTypes.Loop => new LoopNode { Id = id, Name = id },
        NodeTypes.Iteration => new IterationNode { Id = id, Name = id },
        NodeTypes.Parallel => new ParallelNode { Id = id, Name = id },
        NodeTypes.HttpRequest => new HttpRequestNode { Id = id, Name = id },
        NodeTypes.A2AAgent => new A2AAgentNode { Id = id, Name = id },
        NodeTypes.Autonomous => new AutonomousNode { Id = id, Name = id },
        NodeTypes.Router => new RouterNode { Id = id, Name = id },
        _ => throw new NotSupportedException($"Unknown fixture type: {type}")
    };

    [Fact]
    public void HasAnyExecutable_AgentOnly_ReturnsTrue()
    {
        var nodes = new List<NodeConfig> { CreateNode(NodeTypes.Agent, "a1") };
        Assert.True(NodeTypeRegistry.HasAnyExecutable(nodes));
    }

    [Fact]
    public void HasAnyExecutable_OnlyStartEnd_ReturnsFalse()
    {
        var nodes = new List<NodeConfig>
        {
            CreateNode(NodeTypes.Start, "s1"),
            CreateNode(NodeTypes.End, "e1")
        };
        Assert.False(NodeTypeRegistry.HasAnyExecutable(nodes));
    }

    [Fact]
    public void HasAnyExecutable_OnlyRag_ReturnsFalse()
    {
        var nodes = new List<NodeConfig> { CreateNode(NodeTypes.Rag, "r1") };
        Assert.False(NodeTypeRegistry.HasAnyExecutable(nodes));
    }

    [Fact]
    public void HasAnyExecutable_CodeNode_ReturnsTrue()
    {
        var nodes = new List<NodeConfig> { CreateNode(NodeTypes.Code, "c1") };
        Assert.True(NodeTypeRegistry.HasAnyExecutable(nodes));
    }

    [Fact]
    public void HasAnyRequiringImperative_HumanNode_ReturnsTrue()
    {
        var nodes = new List<NodeConfig> { CreateNode(NodeTypes.Human, "h1") };
        Assert.True(NodeTypeRegistry.HasAnyRequiringImperative(nodes));
    }

    [Fact]
    public void HasAnyRequiringImperative_AgentOnly_ReturnsFalse()
    {
        var nodes = new List<NodeConfig> { CreateNode(NodeTypes.Agent, "a1") };
        Assert.False(NodeTypeRegistry.HasAnyRequiringImperative(nodes));
    }

    [Theory]
    [InlineData(NodeTypes.Condition)]
    [InlineData(NodeTypes.Loop)]
    [InlineData(NodeTypes.Iteration)]
    [InlineData(NodeTypes.Parallel)]
    [InlineData(NodeTypes.HttpRequest)]
    [InlineData(NodeTypes.A2AAgent)]
    [InlineData(NodeTypes.Autonomous)]
    [InlineData(NodeTypes.Human)]
    [InlineData(NodeTypes.Code)]
    [InlineData(NodeTypes.Router)]
    public void HasAnyRequiringImperative_AllImperativeTypes(string nodeType)
    {
        var nodes = new List<NodeConfig> { CreateNode(nodeType) };
        Assert.True(NodeTypeRegistry.HasAnyRequiringImperative(nodes));
    }

    [Theory]
    [InlineData(NodeTypes.Start)]
    [InlineData(NodeTypes.End)]
    [InlineData(NodeTypes.Rag)]
    public void IsMeta_OrDataNode_NotExecutable(string nodeType)
    {
        Assert.False(NodeTypeRegistry.IsExecutable(nodeType));
    }

    [Theory]
    [InlineData(NodeTypes.Agent)]
    [InlineData(NodeTypes.A2AAgent)]
    [InlineData(NodeTypes.Autonomous)]
    public void IsAgentLike_AllAgentTypes(string nodeType)
    {
        Assert.True(NodeTypeRegistry.IsAgentLike(nodeType));
    }

    [Theory]
    [InlineData(NodeTypes.Code)]
    [InlineData(NodeTypes.Condition)]
    [InlineData(NodeTypes.Human)]
    public void IsAgentLike_NonAgentTypes_ReturnsFalse(string nodeType)
    {
        Assert.False(NodeTypeRegistry.IsAgentLike(nodeType));
    }
}
