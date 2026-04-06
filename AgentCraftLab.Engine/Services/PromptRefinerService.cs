using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// Prompt 優化服務。使用 LLM + Prompt Engineering 指南來改善使用者的 AI Agent Instructions。
/// </summary>
public class PromptRefinerService
{
    private readonly SkillPromptProvider _promptProvider;
    private readonly ILogger<PromptRefinerService>? _logger;

    private const string RefinerInstruction = """
        你是一位專業的 Prompt Engineer。根據以下 Prompt Engineering 指南，優化使用者提供的 AI Agent 指令（System Instructions）。

        優化原則：
        1. **結構化**：加入明確的角色定義、任務描述、約束條件
        2. **具體化**：將模糊指令改為具體、可執行的指令
        3. **格式化**：使用結構化標籤分隔不同區塊
        4. **範例**：在需要時加入 Few-shot 範例
        5. **安全護欄**：加入必要的限制（不要做什麼、輸出格式等）
        6. **保留意圖**：保留使用者原始意圖，不改變核心目標

        回應格式（純 JSON，不要包含 markdown fence）：
        {
          "refined": "優化後的完整 prompt",
          "changes": ["修改說明1", "修改說明2", ...]
        }

        規則：
        - refined 是完整的優化後 prompt（直接可用，不是 diff）
        - changes 是簡短的修改說明清單（3-8 條）
        - 保持使用者原始語言（繁中 prompt → 繁中優化；英文 → 英文）
        - 如果 prompt 已經很好，仍然嘗試改進，但 changes 可以標註「原始 prompt 已具備此特性」
        """;

    public PromptRefinerService(SkillPromptProvider promptProvider, ILogger<PromptRefinerService>? logger = null)
    {
        _promptProvider = promptProvider;
        _logger = logger;
    }

    /// <summary>
    /// 使用 LLM 優化 prompt。
    /// </summary>
    /// <param name="client">LLM 客戶端。</param>
    /// <param name="prompt">使用者的原始 prompt。</param>
    /// <param name="model">使用者選擇的模型名稱。</param>
    /// <param name="provider">Provider 名稱（如 "openai"、"anthropic"），用於載入模型專屬指南。</param>
    /// <param name="ct">取消 token。</param>
    public async Task<PromptRefinerResult> RefineAsync(
        IChatClient client, string prompt, string model, string? provider, CancellationToken ct)
    {
        var guide = _promptProvider.LoadPrompt("prompt-refiner", model, provider);
        var systemPrompt = RefinerInstruction + "\n\n---\n\n" + guide;

        _logger?.LogInformation("[REFINER] Optimizing prompt ({Length} chars) with model-specific guide for {Model}",
            prompt.Length, model);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, $"請優化以下 prompt：\n\n{prompt}"),
        };

        var response = await client.GetResponseAsync(messages,
            new ChatOptions { Temperature = 0.3f }, ct);

        var responseText = response.Text ?? "";
        return ParseResult(responseText, prompt);
    }

    /// <summary>從 LLM 回應解析結果。</summary>
    private PromptRefinerResult ParseResult(string responseText, string original)
    {
        try
        {
            // 嘗試從回應中提取 JSON
            var jsonMatch = Regex.Match(responseText, @"\{[\s\S]*""refined""[\s\S]*\}",
                RegexOptions.None, TimeSpan.FromSeconds(2));

            if (jsonMatch.Success)
            {
                var json = JsonSerializer.Deserialize<JsonElement>(jsonMatch.Value);
                var refined = json.TryGetProperty("refined", out var r) ? r.GetString() ?? "" : "";
                var changes = new List<string>();
                if (json.TryGetProperty("changes", out var c) && c.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in c.EnumerateArray())
                    {
                        var text = item.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            changes.Add(text);
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(refined))
                {
                    return new PromptRefinerResult(original, refined, changes);
                }
            }

            // Fallback：整段回應作為 refined
            _logger?.LogWarning("[REFINER] Failed to parse JSON from response, using raw text as refined prompt");
            return new PromptRefinerResult(original, responseText.Trim(), ["LLM 回應格式非預期，已使用原始回應"]);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[REFINER] Error parsing response: {Error}", ex.Message);
            return new PromptRefinerResult(original, responseText.Trim(), ["解析錯誤，已使用原始回應"]);
        }
    }
}

/// <summary>Prompt 優化結果。</summary>
public record PromptRefinerResult(string Original, string Refined, List<string> Changes);
