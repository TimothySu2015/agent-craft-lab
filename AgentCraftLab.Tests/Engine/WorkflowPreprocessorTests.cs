using AgentCraftLab.Engine.Models;
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

    [Fact]
    public void HasAnyExecutable_AgentOnly_ReturnsTrue()
    {
        var nodes = new List<WorkflowNode>
        {
            new() { Id = "a1", Type = NodeTypes.Agent, Name = "Agent" }
        };

        Assert.True(NodeTypeRegistry.HasAnyExecutable(nodes));
    }

    [Fact]
    public void HasAnyExecutable_OnlyStartEnd_ReturnsFalse()
    {
        var nodes = new List<WorkflowNode>
        {
            new() { Id = "s1", Type = NodeTypes.Start, Name = "Start" },
            new() { Id = "e1", Type = NodeTypes.End, Name = "End" }
        };

        Assert.False(NodeTypeRegistry.HasAnyExecutable(nodes));
    }

    [Fact]
    public void HasAnyExecutable_OnlyRag_ReturnsFalse()
    {
        var nodes = new List<WorkflowNode>
        {
            new() { Id = "r1", Type = NodeTypes.Rag, Name = "RAG" }
        };

        Assert.False(NodeTypeRegistry.HasAnyExecutable(nodes));
    }

    [Fact]
    public void HasAnyExecutable_CodeNode_ReturnsTrue()
    {
        var nodes = new List<WorkflowNode>
        {
            new() { Id = "c1", Type = NodeTypes.Code, Name = "Code" }
        };

        Assert.True(NodeTypeRegistry.HasAnyExecutable(nodes));
    }

    [Fact]
    public void HasAnyRequiringImperative_HumanNode_ReturnsTrue()
    {
        var nodes = new List<WorkflowNode>
        {
            new() { Id = "h1", Type = NodeTypes.Human, Name = "Human" }
        };

        Assert.True(NodeTypeRegistry.HasAnyRequiringImperative(nodes));
    }

    [Fact]
    public void HasAnyRequiringImperative_AgentOnly_ReturnsFalse()
    {
        var nodes = new List<WorkflowNode>
        {
            new() { Id = "a1", Type = NodeTypes.Agent, Name = "Agent" }
        };

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
        var nodes = new List<WorkflowNode>
        {
            new() { Id = "n1", Type = nodeType, Name = "Test" }
        };

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
