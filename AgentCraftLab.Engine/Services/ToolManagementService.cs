using System.Diagnostics;
using AgentCraftLab.Engine.Models;
using MailKit.Security;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 工具管理服務：統一工具狀態查詢、憑證規格、格式驗證與健康檢查。
/// </summary>
public class ToolManagementService
{
    private readonly ToolRegistryService _registry;
    private readonly IHttpClientFactory _httpFactory;

    private static readonly Dictionary<string, CredentialSpec> CredentialSpecs = new()
    {
        ["azure-openai"] = new("azure-openai", "Azure OpenAI", [
            new("ApiKey", "API Key", Required: true, Placeholder: "your-api-key"),
            new("Endpoint", "Endpoint URL", Required: true, IsSensitive: false, Placeholder: "https://your-resource.openai.azure.com/"),
            new("Model", "模型名稱", Required: false, IsSensitive: false, Placeholder: "gpt-4o-mini"),
        ]),
        ["tavily"] = new("tavily", "Tavily", [
            new("ApiKey", "API Key", Required: true, Placeholder: "tvly-..."),
        ]),
        ["brave"] = new("brave", "Brave Search", [
            new("ApiKey", "API Key", Required: true, Placeholder: "BSA..."),
        ]),
        ["serper"] = new("serper", "Serper", [
            new("ApiKey", "API Key", Required: true, Placeholder: "your-serper-key"),
        ]),
        ["smtp"] = new("smtp", "SMTP Email", [
            new("Endpoint", "主機:埠號", Required: true, IsSensitive: false, Placeholder: "smtp.gmail.com:587"),
            new("Model", "寄件者 Email", Required: true, IsSensitive: false, Placeholder: "you@gmail.com"),
            new("ApiKey", "密碼 / App Password", Required: true, Placeholder: "app-password"),
        ]),
    };

    public ToolManagementService(ToolRegistryService registry, IHttpClientFactory httpFactory)
    {
        _registry = registry;
        _httpFactory = httpFactory;
    }

    /// <summary>
    /// 取得指定 provider 的憑證欄位規格。
    /// </summary>
    public CredentialSpec? GetCredentialSpec(string provider)
        => CredentialSpecs.GetValueOrDefault(provider);

    /// <summary>
    /// 取得所有 provider 的憑證規格。
    /// </summary>
    public IReadOnlyList<CredentialSpec> GetAllCredentialSpecs()
        => CredentialSpecs.Values.ToList();

    /// <summary>
    /// 取得所有工具的狀態清單。
    /// </summary>
    public IReadOnlyList<ToolStatus> GetToolStatusList(
        Dictionary<string, ProviderCredential> credentials,
        IReadOnlySet<string> enabledToolIds)
    {
        var allTools = _registry.GetAvailableTools();
        var result = new List<ToolStatus>(allTools.Count);

        foreach (var tool in allTools)
        {
            var isEnabled = enabledToolIds.Contains(tool.Id);
            var isCredConfigured = tool.RequiredCredential is null ||
                                   _registry.IsToolAvailable(tool.Id, credentials);

            var availability = !isEnabled
                ? ToolAvailability.Disabled
                : !isCredConfigured
                    ? ToolAvailability.MissingCredential
                    : ToolAvailability.Ready;

            result.Add(new ToolStatus(
                tool.Id, tool.DisplayName, tool.Category, tool.Icon,
                tool.Description, tool.RequiredCredential,
                isCredConfigured, isEnabled, availability));
        }

        return result;
    }

    /// <summary>
    /// 同步格式檢查（必填欄位 + URI 格式）。
    /// </summary>
    public (bool Valid, string? Error) ValidateCredentialFormat(string provider, ProviderCredential credential)
    {
        if (!CredentialSpecs.TryGetValue(provider, out var spec))
        {
            return (false, $"未知的 provider：{provider}");
        }

        foreach (var field in spec.Fields.Where(f => f.Required))
        {
            var value = field.FieldName switch
            {
                "ApiKey" => credential.ApiKey,
                "Endpoint" => credential.Endpoint,
                "Model" => credential.Model,
                _ => ""
            };

            if (string.IsNullOrWhiteSpace(value))
            {
                return (false, $"缺少必填欄位：{field.Label}");
            }
        }

        // Endpoint 欄位的 URI 格式檢查（azure-openai 的 Endpoint 必須是 URL）
        if (provider == "azure-openai" && !string.IsNullOrWhiteSpace(credential.Endpoint))
        {
            if (!Uri.TryCreate(credential.Endpoint, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                return (false, $"Endpoint 格式無效，需為 HTTP/HTTPS URL：{credential.Endpoint}");
            }
        }

        return (true, null);
    }

    /// <summary>
    /// 格式檢查 + 輕量 API 連線測試。
    /// </summary>
    /// <param name="skipConnectionTest">true 時只做格式驗證，跳過實際連線（內部測試用）。預設 false（必須通過連線測試）。</param>
    public async Task<(bool Valid, string? Error)> ValidateCredentialAsync(
        string provider, ProviderCredential credential, bool skipConnectionTest = false, CancellationToken ct = default)
    {
        var (formatValid, formatError) = ValidateCredentialFormat(provider, credential);
        if (!formatValid)
        {
            return (false, formatError);
        }

        if (skipConnectionTest)
        {
            return (true, null);
        }

        try
        {
            switch (provider)
            {
                case "azure-openai":
                {
                    var endpoint = credential.Endpoint.TrimEnd('/');
                    var client = _httpFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(10);
                    using var req = new HttpRequestMessage(HttpMethod.Get,
                        $"{endpoint}/openai/models?api-version=2024-10-21");
                    req.Headers.Add("api-key", credential.ApiKey);
                    using var resp = await client.SendAsync(req, ct);
                    if (!resp.IsSuccessStatusCode)
                    {
                        return (false, $"Azure OpenAI 連線失敗：HTTP {(int)resp.StatusCode}");
                    }

                    return (true, null);
                }

                case "smtp":
                {
                    var parts = credential.Endpoint.Split(':', 2);
                    var host = parts[0];
                    var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 587;
                    var sslOption = port switch
                    {
                        465 => SecureSocketOptions.SslOnConnect,
                        25 => SecureSocketOptions.None,
                        _ => SecureSocketOptions.StartTls
                    };

                    using var smtp = new MailKit.Net.Smtp.SmtpClient();
                    smtp.Timeout = 10_000;
                    await smtp.ConnectAsync(host, port, sslOption, ct);
                    await smtp.AuthenticateAsync(credential.Model, credential.ApiKey, ct);
                    await smtp.DisconnectAsync(quit: true, ct);
                    return (true, null);
                }

                default:
                {
                    // tavily/brave/serper：委派 ToolRegistryService.TestToolAsync
                    var toolId = provider switch
                    {
                        "tavily" => "tavily_search",
                        "brave" => "brave_search",
                        "serper" => "serper_search",
                        _ => null
                    };

                    if (toolId is null)
                    {
                        return (true, null);
                    }

                    var testInput = ToolRegistryService.GetDefaultTestInput(toolId);
                    var creds = new Dictionary<string, ProviderCredential> { [provider] = credential };
                    var (success, result) = await _registry.TestToolAsync(toolId, testInput, creds);
                    return success ? (true, null) : (false, $"API 測試失敗：{result}");
                }
            }
        }
        catch (Exception ex)
        {
            return (false, $"連線測試異常：{ex.Message}");
        }
    }

    /// <summary>
    /// 批次健康檢查（Task.WhenAll 並行）。
    /// </summary>
    public async Task<IReadOnlyList<ToolHealthResult>> HealthCheckAsync(
        IEnumerable<string> toolIds,
        Dictionary<string, ProviderCredential> credentials,
        CancellationToken ct = default)
    {
        var allTools = _registry.GetAvailableTools().ToDictionary(t => t.Id);
        var tasks = new List<Task<ToolHealthResult>>();

        foreach (var toolId in toolIds)
        {
            if (!allTools.TryGetValue(toolId, out var tool))
            {
                continue;
            }

            tasks.Add(CheckSingleToolAsync(tool, credentials, ct));
        }

        return await Task.WhenAll(tasks);
    }

    private async Task<ToolHealthResult> CheckSingleToolAsync(
        ToolDefinition tool, Dictionary<string, ProviderCredential> credentials, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var testInput = ToolRegistryService.GetDefaultTestInput(tool.Id);
            var (success, result) = await _registry.TestToolAsync(tool.Id, testInput, credentials);
            sw.Stop();
            var message = success
                ? result.Length > 100 ? result[..100] + "..." : result
                : result;
            return new ToolHealthResult(tool.Id, tool.DisplayName, success, message, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ToolHealthResult(tool.Id, tool.DisplayName, false, ex.Message, sw.ElapsedMilliseconds);
        }
    }
}
