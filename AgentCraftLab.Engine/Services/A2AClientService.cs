using System.ComponentModel;
using System.Text.Json;
using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// A2A (Agent-to-Agent) 協定客戶端。
/// 支援 Google A2A（JSON-RPC）和 Microsoft（REST）雙格式。
/// </summary>
public class A2AClientService : IA2AClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<A2AClientService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.A2AOptions;

    public A2AClientService(IHttpClientFactory httpFactory, ILogger<A2AClientService> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>
    /// 發現 remote A2A agent 的 Agent Card。
    /// </summary>
    public async Task<A2AAgentCard> DiscoverAsync(string baseUrl, string format = "auto", CancellationToken ct = default)
    {
        using var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(Timeouts.DiscoverySeconds);
        var url = baseUrl.TrimEnd('/');

        if (format is "google" or "auto")
        {
            var card = await TryDiscoverGoogleAsync(http, url, ct);
            if (card is not null)
            {
                return card;
            }
        }

        if (format is "microsoft" or "auto")
        {
            var card = await TryDiscoverMicrosoftAsync(http, url, ct);
            if (card is not null)
            {
                return card;
            }
        }

        // fallback: 從 URL 推斷
        return new A2AAgentCard
        {
            Name = new Uri(url).Host,
            Description = $"A2A Agent at {url}",
            Version = "unknown",
            BaseUrl = url
        };
    }

    /// <summary>
    /// 將 A2A agent 包裝為 AITool，讓 LLM 可以呼叫。
    /// </summary>
    public AITool WrapAsAITool(A2AAgentCard card, string format = "auto")
    {
        var capturedUrl = card.BaseUrl;
        var capturedFormat = format;

        return AIFunctionFactory.Create(
            async ([Description("要傳送給遠端 Agent 的訊息")] string message) =>
                await SendMessageAsync(capturedUrl, message, format: capturedFormat),
            name: $"a2a_{NameUtils.Sanitize(card.Name)}",
            description: $"呼叫遠端 A2A Agent: {card.Name}. {card.Description}"
        );
    }

    /// <summary>
    /// 發送訊息給 A2A agent 並取得回應。
    /// </summary>
    public async Task<string> SendMessageAsync(string baseUrl, string message, string? contextId = null, string format = "auto", int? timeoutSeconds = null)
    {
        try
        {
            var timeout = timeoutSeconds ?? Timeouts.ToolCallSeconds;

            if (format is "google" or "auto")
            {
                var result = await TrySendGoogleAsync(baseUrl, message, contextId, timeout);
                if (result is not null)
                {
                    return result;
                }
            }

            if (format is "microsoft" or "auto")
            {
                var result = await TrySendMicrosoftAsync(baseUrl, message, contextId, timeout);
                if (result is not null)
                {
                    return result;
                }
            }

            return "A2A call failed: no compatible endpoint found";
        }
        catch (Exception ex)
        {
            return $"A2A call failed: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════
    // Google A2A Format
    // ═══════════════════════════════════════════

    private async Task<A2AAgentCard?> TryDiscoverGoogleAsync(HttpClient http, string url, CancellationToken ct)
    {
        try
        {
            // 先試 /agent-card.json（子路徑格式），再試 /.well-known/agent-card.json
            foreach (var cardPath in new[] { $"{url}/agent-card.json", $"{url}/.well-known/agent-card.json" })
            {
                try
                {
                    var resp = await http.GetStringAsync(cardPath, ct);
                    var card = JsonSerializer.Deserialize<A2AServerAgentCard>(resp, JsonOptions);
                    if (card is not null && !string.IsNullOrEmpty(card.Name))
                    {
                        return new A2AAgentCard
                        {
                            Name = card.Name,
                            Description = card.Description,
                            Version = card.Version,
                            BaseUrl = card.Url ?? url
                        };
                    }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "[A2A Client] Discovery attempt failed: {Url}", cardPath); }
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "[A2A Client] Google discovery failed for {Url}", url); }
        return null;
    }

    private async Task<string?> TrySendGoogleAsync(string baseUrl, string message, string? contextId, int timeoutSeconds = 30)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            var url = baseUrl.TrimEnd('/');

            var rpcRequest = new JsonRpcRequest
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Method = A2AProtocol.MethodSend,
                Params = new JsonRpcParams
                {
                    Message = new A2AMessage
                    {
                        Role = "user",
                        ContextId = contextId ?? Guid.NewGuid().ToString("N")[..8],
                        Parts = [A2APart.TextPart(message)]
                    }
                }
            };

            var payload = JsonSerializer.Serialize(rpcRequest, JsonOptions);
            var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            var response = await http.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return null; // fallback to Microsoft format
            }

            var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            // JSON-RPC response: result.artifacts[].parts[].text
            if (root.TryGetProperty("result", out var result))
            {
                if (result.TryGetProperty("artifacts", out var artifacts))
                {
                    var texts = new List<string>();
                    foreach (var artifact in artifacts.EnumerateArray())
                    {
                        var t = ExtractPartsText(artifact);
                        if (t is not null)
                        {
                            texts.Add(t);
                        }
                    }

                    if (texts.Count > 0)
                    {
                        return string.Join("\n", texts);
                    }
                }

                // result.status.message.parts[].text
                if (result.TryGetProperty("status", out var status) &&
                    status.TryGetProperty("message", out var statusMsg))
                {
                    var t = ExtractPartsText(statusMsg);
                    if (t is not null)
                    {
                        return t;
                    }
                }
            }

            // Check for error
            if (root.TryGetProperty("error", out var error))
            {
                var errMsg = error.TryGetProperty("message", out var em) ? em.GetString() : "Unknown error";
                return $"A2A error: {errMsg}";
            }

            return StringUtils.Truncate(responseText, 500);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning("[A2A Client] Google send timed out for {Url} ({Timeout}s)", baseUrl, timeoutSeconds);
            return $"A2A call timed out after {timeoutSeconds}s (Google format)";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[A2A Client] Google send failed for {Url}", baseUrl);
            return null;
        }
    }

    // ═══════════════════════════════════════════
    // Microsoft Format
    // ═══════════════════════════════════════════

    private async Task<A2AAgentCard?> TryDiscoverMicrosoftAsync(HttpClient http, string url, CancellationToken ct)
    {
        try
        {
            var cardUrl = $"{url}/v1/card";
            var resp = await http.GetStringAsync(cardUrl, ct);
            var card = JsonSerializer.Deserialize<A2AAgentCard>(resp, JsonOptions);
            if (card is not null)
            {
                card.BaseUrl = url;
                return card;
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "[A2A Client] Microsoft discovery failed for {Url}", url); }
        return null;
    }

    private async Task<string?> TrySendMicrosoftAsync(string baseUrl, string message, string? contextId, int timeoutSeconds = 30)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            var url = $"{baseUrl.TrimEnd('/')}/v1/message:send";

            var payload = JsonSerializer.Serialize(new
            {
                message = new
                {
                    kind = "message",
                    role = "user",
                    parts = new[] { new { kind = "text", text = message, metadata = new { } } },
                    messageId = (string?)null,
                    contextId = contextId ?? Guid.NewGuid().ToString("N")[..8]
                }
            });

            var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            var response = await http.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return $"A2A request failed ({response.StatusCode}): {StringUtils.Truncate(responseText, 200)}";
            }

            var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            var text = ExtractPartsText(root);
            if (text is not null)
            {
                return text;
            }

            if (root.TryGetProperty("result", out var result))
            {
                text = ExtractPartsText(result);
                if (text is not null)
                {
                    return text;
                }
            }

            return StringUtils.Truncate(responseText, 500);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning("[A2A Client] Microsoft send timed out for {Url} ({Timeout}s)", baseUrl, timeoutSeconds);
            return $"A2A call timed out after {timeoutSeconds}s (Microsoft format)";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[A2A Client] Microsoft send failed for {Url}", baseUrl);
            return null;
        }
    }

    private static string? ExtractPartsText(JsonElement element)
    {
        if (!element.TryGetProperty("parts", out var parts))
        {
            return null;
        }

        var texts = new List<string>();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var txt))
            {
                texts.Add(txt.GetString() ?? "");
            }
        }

        return texts.Count > 0 ? string.Join("\n", texts) : null;
    }
}
