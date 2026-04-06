using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;
using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 輕量 MCP (Model Context Protocol) 客戶端。
/// 支援 Streamable HTTP 傳輸（/mcp endpoint）。
/// </summary>
public class McpClientService : IMcpClient
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly IHttpClientFactory _httpFactory;

    public McpClientService(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    /// <summary>
    /// 連接 MCP Server 並取得可用工具，轉換為 AITool 清單。
    /// </summary>
    public async Task<IList<AITool>> GetToolsAsync(string serverUrl, CancellationToken ct = default)
    {
        using var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(Timeouts.HttpApiSeconds);
        var url = serverUrl.TrimEnd('/');

        // 1. Initialize
        var initResp = await PostMcp(http, url, null, new
        {
            jsonrpc = "2.0", id = 1, method = "initialize",
            @params = new
            {
                protocolVersion = McpProtocol.Version,
                capabilities = new { },
                clientInfo = new { name = "AgentCraftLab", version = "1.0" }
            }
        }, ct);

        var sessionId = initResp.SessionId;
        if (string.IsNullOrEmpty(sessionId))
            throw new Exception("MCP Server did not return a session ID.");

        // 2. Send initialized notification
        await PostMcp(http, url, sessionId, new
        {
            jsonrpc = "2.0", method = "notifications/initialized", @params = new { }
        }, ct);

        // 3. List tools
        var toolsResp = await PostMcp(http, url, sessionId, new
        {
            jsonrpc = "2.0", id = 3, method = "tools/list", @params = new { }
        }, ct);

        var doc = JsonDocument.Parse(toolsResp.Body);
        var result = doc.RootElement.GetProperty("result");
        var tools = result.GetProperty("tools");

        var aiTools = new List<AITool>();
        foreach (var tool in tools.EnumerateArray())
        {
            var name = tool.GetProperty("name").GetString() ?? "";
            var desc = tool.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            var inputSchema = tool.TryGetProperty("inputSchema", out var s) ? s : default;

            // 解析參數名稱
            var paramNames = new List<string>();
            if (inputSchema.ValueKind == JsonValueKind.Object &&
                inputSchema.TryGetProperty("properties", out var props))
            {
                foreach (var prop in props.EnumerateObject())
                    paramNames.Add(prop.Name);
            }

            // 建立 closure 捕獲 serverUrl, sessionId, toolName
            var capturedUrl = url;
            var capturedSession = sessionId;
            var capturedName = name;
            // 建立通用的 MCP tool 呼叫函式
            var func = AIFunctionFactory.Create(
                async ([Description("Tool arguments as JSON string")] string argsJson) =>
                {
                    return await CallToolAsync(capturedUrl, capturedSession, capturedName, argsJson);
                },
                name: capturedName.Replace("-", "_"),
                description: desc
            );

            aiTools.Add(func);
        }

        return aiTools;
    }

    /// <summary>
    /// 呼叫 MCP 工具。
    /// </summary>
    private async Task<string> CallToolAsync(string serverUrl, string sessionId, string toolName, string argsJson)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(Timeouts.ToolCallSeconds);

            var arguments = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(argsJson))
            {
                try
                {
                    arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(argsJson, JsonOpts)
                                ?? new Dictionary<string, object?>();
                }
                catch
                {
                    // 如果不是 JSON，當作單一字串參數
                    arguments["input"] = argsJson;
                }
            }

            var resp = await PostMcp(http, serverUrl, sessionId, new
            {
                jsonrpc = "2.0", id = 10, method = "tools/call",
                @params = new { name = toolName, arguments }
            });

            var doc = JsonDocument.Parse(resp.Body);
            if (doc.RootElement.TryGetProperty("result", out var result))
            {
                if (result.TryGetProperty("content", out var content))
                {
                    var texts = new List<string>();
                    foreach (var item in content.EnumerateArray())
                    {
                        if (item.TryGetProperty("text", out var txt))
                            texts.Add(txt.GetString() ?? "");
                    }
                    return texts.Count > 0 ? string.Join("\n", texts) : result.ToString();
                }
                return result.ToString();
            }

            if (doc.RootElement.TryGetProperty("error", out var error))
                return $"MCP Error: {error}";

            return resp.Body;
        }
        catch (Exception ex)
        {
            return $"MCP tool call failed: {ex.Message}";
        }
    }

    /// <summary>
    /// 發送 MCP JSON-RPC 請求並解析 SSE 回應。
    /// </summary>
    private async Task<(string SessionId, string Body)> PostMcp(
        HttpClient http, string url, string? sessionId, object payload, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (!string.IsNullOrEmpty(sessionId))
            request.Headers.Add("Mcp-Session-Id", sessionId);

        var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var sid = response.Headers.TryGetValues("mcp-session-id", out var vals)
            ? vals.FirstOrDefault() ?? sessionId ?? ""
            : sessionId ?? "";

        // 解析 SSE 格式：合併所有 data: 行
        if (body.Contains("data: "))
        {
            var dataLines = body.Split('\n')
                .Where(l => l.StartsWith("data: "))
                .Select(l => l[6..].TrimEnd('\r'));
            var joined = string.Join("", dataLines);
            if (!string.IsNullOrEmpty(joined))
                body = joined;
        }

        return (sid, body);
    }
}
