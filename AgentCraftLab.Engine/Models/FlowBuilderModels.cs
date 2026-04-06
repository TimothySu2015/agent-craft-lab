namespace AgentCraftLab.Engine.Models;

/// <summary>
/// AI Build 模式的請求模型。
/// </summary>
public class FlowBuildRequest
{
    /// <summary>使用者的自然語言描述。</summary>
    public string UserMessage { get; set; } = "";

    /// <summary>目前畫布上的 Workflow JSON（用於漸進修改）。</summary>
    public string? CurrentWorkflowJson { get; set; }

    /// <summary>對話歷史。</summary>
    public List<ChatHistoryEntry> History { get; set; } = [];

    /// <summary>LLM 提供者的憑證。</summary>
    public ProviderCredential Credential { get; set; } = new();

    /// <summary>使用的模型名稱。</summary>
    public string Model { get; set; } = Defaults.Model;

    /// <summary>LLM 提供者名稱（openai, azure-openai, ollama, anthropic 等）。</summary>
    public string Provider { get; set; } = Providers.OpenAI;
}
