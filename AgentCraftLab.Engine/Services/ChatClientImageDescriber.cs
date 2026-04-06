using AgentCraftLab.Cleaner.Abstractions;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 將 MEAI IChatClient（多模態）橋接到 CraftCleaner 的 IImageDescriber。
/// 使用 DataContent 傳送圖片，讓 GPT-4o / Claude / Gemini 等多模態模型產生描述。
/// </summary>
public sealed class ChatClientImageDescriber : IImageDescriber
{
    private readonly IChatClient _chatClient;
    private readonly string _systemPrompt;

    public ChatClientImageDescriber(IChatClient chatClient, string? systemPrompt = null)
    {
        _chatClient = chatClient;
        _systemPrompt = systemPrompt ?? DefaultSystemPrompt;
    }

    public async Task<ImageDescriptionResult> DescribeAsync(
        byte[] imageData,
        string mimeType,
        ImageDescriptionContext? context = null,
        CancellationToken ct = default)
    {
        var userContent = new List<AIContent>
        {
            new TextContent(BuildUserPrompt(context)),
            new DataContent(imageData, mimeType),
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, _systemPrompt),
            new(ChatRole.User, userContent),
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        var usage = response.Usage;

        return new ImageDescriptionResult
        {
            Description = response.Text ?? "[No description generated]",
            Confidence = 1.0f, // 多模態 LLM 無原生信心度，預設視為可信
            InputTokens = (int)(usage?.InputTokenCount ?? 0),
            OutputTokens = (int)(usage?.OutputTokenCount ?? 0),
        };
    }

    private static string BuildUserPrompt(ImageDescriptionContext? context)
    {
        if (context is null)
        {
            return "Please describe this image.";
        }

        var parts = new List<string> { "Please describe this image." };

        if (!string.IsNullOrWhiteSpace(context.PageTitle))
        {
            parts.Add($"Page/Slide title: {context.PageTitle}");
        }

        if (!string.IsNullOrWhiteSpace(context.PageText))
        {
            parts.Add($"Surrounding text:\n{context.PageText}");
        }

        if (context.PageNumber.HasValue)
        {
            parts.Add($"Page/Slide number: {context.PageNumber}");
        }

        return string.Join("\n\n", parts);
    }

    private const string DefaultSystemPrompt = """
        You are an expert image analyst. Describe the content of images found in documents.

        Instructions:
        - Describe what you see (chart, diagram, flowchart, screenshot, photo, etc.)
        - For charts/graphs: describe axes, data points, trends, and key numbers
        - For diagrams/flowcharts: describe how elements connect and relate
        - Include any visible text labels or annotations
        - Use the surrounding text context to produce more accurate descriptions
        - Be concise but complete
        - Match the language of the surrounding document context when provided
        """;
}
