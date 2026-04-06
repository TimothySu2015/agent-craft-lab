using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// HTTP API 工具服務：將使用者自訂的 REST API 包裝為 AITool。
/// </summary>
public class HttpApiToolService : IHttpApiTool
{
    private static readonly JsonSerializerOptions CaseInsensitiveOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true
    };

    private readonly IHttpClientFactory _httpFactory;

    public HttpApiToolService(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }
    /// <summary>
    /// 將 HttpApiDefinition 包裝為 AITool，LLM 可根據 description 決定何時呼叫。
    /// </summary>
    public AITool WrapAsAITool(HttpApiDefinition api)
    {
        var capturedApi = api;
        return AIFunctionFactory.Create(
            async ([Description("API parameters as JSON")] string argsJson) =>
                await CallApiAsync(capturedApi, argsJson),
            name: NameUtils.Sanitize(api.Name),
            description: api.Description
        );
    }

    /// <summary>
    /// 呼叫 HTTP API。
    /// </summary>
    public async Task<string> CallApiAsync(HttpApiDefinition api, string argsJson)
    {
        try
        {
            // SSRF 防護
            var (isSafe, ssrfError) = await SafeUrlValidator.ValidateAsync(api.Url);
            if (!isSafe) return $"[Blocked] {ssrfError}";

            using var http = _httpFactory.CreateClient();
            var timeout = api.TimeoutSeconds > 0 ? api.TimeoutSeconds : Timeouts.HttpApiSeconds;
            http.Timeout = TimeSpan.FromSeconds(timeout);

            // 解析參數
            var args = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(argsJson))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(argsJson,
                        CaseInsensitiveOptions);
                    if (parsed != null)
                        foreach (var kv in parsed)
                            args[kv.Key] = kv.Value?.ToString() ?? "";
                }
                catch
                {
                    // 如果不是 JSON，當作單一參數
                    var paramNames = ExtractParamNames(api.Url);
                    if (paramNames.Count > 0)
                        args[paramNames[0]] = argsJson;
                }
            }

            // 替換 URL 模板中的 {param}
            // 如果 LLM 傳的 key 跟 URL 模板不同，自動對應（按順序映射）
            var url = api.Url;
            var urlParams = ExtractParamNames(api.Url);
            var argValues = args.Values.ToList();

            // 先嘗試精確匹配
            foreach (var kv in args)
                url = url.Replace($"{{{kv.Key}}}", Uri.EscapeDataString(kv.Value));

            // 如果還有未替換的 {param}，按順序用 arg values 填入
            for (int i = 0; i < urlParams.Count; i++)
            {
                var placeholder = $"{{{urlParams[i]}}}";
                if (url.Contains(placeholder) && i < argValues.Count)
                    url = url.Replace(placeholder, Uri.EscapeDataString(argValues[i]));
            }

            // 設定 Headers
            if (!string.IsNullOrWhiteSpace(api.Headers))
            {
                foreach (var line in api.Headers.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length == 2)
                        http.DefaultRequestHeaders.TryAddWithoutValidation(parts[0].Trim(), parts[1].Trim());
                }
            }

            // Auth 預設注入
            ApplyAuth(http, api, ref url);

            var method = (api.Method ?? "GET").ToUpperInvariant();

            // 準備 body（POST/PUT/PATCH 用）
            string? bodyStr = null;
            if (method is "POST" or "PUT" or "PATCH")
            {
                bodyStr = api.BodyTemplate ?? "";
                foreach (var kv in args)
                {
                    var escaped = JsonSerializer.Serialize(kv.Value).Trim('"');
                    bodyStr = bodyStr.Replace($"{{{kv.Key}}}", escaped);
                }
            }

            // 發送請求（含重試）
            var (response, responseText) = await SendWithRetryAsync(http, method, url, bodyStr, api);

            if (!response.IsSuccessStatusCode)
                return $"HTTP {(int)response.StatusCode}: {StringUtils.Truncate(responseText, 300)}";

            // 回應解析
            var result = ParseResponse(responseText, api);

            // 截取回應（ResponseMaxLength=0 時不截斷）
            var maxLen = api.ResponseMaxLength > 0 ? api.ResponseMaxLength : int.MaxValue;
            return StringUtils.Truncate(result, maxLen, "...(truncated)");
        }
        catch (Exception ex)
        {
            return $"HTTP API call failed: {ex.Message}";
        }
    }

    /// <summary>
    /// 依 AuthMode 注入認證 header 或 query parameter。
    /// </summary>
    private static void ApplyAuth(HttpClient http, HttpApiDefinition api, ref string url)
    {
        if (string.IsNullOrWhiteSpace(api.AuthCredential))
            return;

        switch (api.AuthMode?.ToLowerInvariant())
        {
            case "bearer":
                http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {api.AuthCredential}");
                break;
            case "basic":
                var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(api.AuthCredential));
                http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Basic {base64}");
                break;
            case "apikey-header":
                var headerName = string.IsNullOrWhiteSpace(api.AuthKeyName) ? "X-Api-Key" : api.AuthKeyName;
                http.DefaultRequestHeaders.TryAddWithoutValidation(headerName, api.AuthCredential);
                break;
            case "apikey-query":
                var queryName = string.IsNullOrWhiteSpace(api.AuthKeyName) ? "api_key" : api.AuthKeyName;
                var separator = url.Contains('?') ? "&" : "?";
                url = $"{url}{separator}{Uri.EscapeDataString(queryName)}={Uri.EscapeDataString(api.AuthCredential)}";
                break;
        }
    }

    /// <summary>
    /// 發送 HTTP 請求，支援 429/5xx 重試 + 指數退避。
    /// </summary>
    private static async Task<(HttpResponseMessage Response, string Text)> SendWithRetryAsync(
        HttpClient http, string method, string url, string? body, HttpApiDefinition api)
    {
        var maxAttempts = api.RetryCount + 1;
        var delay = api.RetryDelayMs > 0 ? api.RetryDelayMs : 1000;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var response = await SendOnceAsync(http, method, url, body, api.ContentType);
            var statusCode = (int)response.StatusCode;

            // 只在 429（Too Many Requests）或 5xx 時重試
            if (attempt < maxAttempts - 1 && (statusCode == 429 || statusCode >= 500))
            {
                response.Dispose();
                await Task.Delay(delay * (1 << attempt)); // 指數退避
                continue;
            }

            var text = await response.Content.ReadAsStringAsync();
            return (response, text);
        }

        // Unreachable，但編譯器需要
        return (new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError), "[Retry exhausted]");
    }

    private static async Task<HttpResponseMessage> SendOnceAsync(
        HttpClient http, string method, string url, string? body, string? contentType)
    {
        if (method is "POST" or "PUT" or "PATCH" && body is not null)
        {
            var content = BuildContent(body, contentType);
            return method switch
            {
                "PUT" => await http.PutAsync(url, content),
                "PATCH" => await http.PatchAsync(url, content),
                _ => await http.PostAsync(url, content)
            };
        }

        if (method == "DELETE")
            return await http.DeleteAsync(url);

        return await http.GetAsync(url);
    }

    /// <summary>
    /// 依 ResponseFormat 解析回應文字。
    /// </summary>
    private static string ParseResponse(string responseText, HttpApiDefinition api)
    {
        switch (api.ResponseFormat?.ToLowerInvariant())
        {
            case "json":
                try
                {
                    using var doc = JsonDocument.Parse(responseText);
                    return JsonSerializer.Serialize(doc.RootElement, IndentedOptions);
                }
                catch
                {
                    return responseText; // 不是有效 JSON，原樣回傳
                }

            case "jsonpath":
                if (string.IsNullOrWhiteSpace(api.ResponseJsonPath))
                    return responseText;
                try
                {
                    return ExtractJsonPath(responseText, api.ResponseJsonPath);
                }
                catch
                {
                    return $"[JSONPath Error] Failed to extract '{api.ResponseJsonPath}' from response";
                }

            default:
                return responseText;
        }
    }

    /// <summary>
    /// 簡易 JSONPath 提取 — 支援點分隔路徑和陣列索引（如 data.items[0].name）。
    /// </summary>
    private static string ExtractJsonPath(string json, string path)
    {
        using var doc = JsonDocument.Parse(json);
        var current = doc.RootElement;

        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            // 解析 array index（如 items[0]）
            var bracketIdx = segment.IndexOf('[');
            if (bracketIdx >= 0)
            {
                var propName = segment[..bracketIdx];
                if (!string.IsNullOrEmpty(propName))
                    current = current.GetProperty(propName);

                var idxStr = segment[(bracketIdx + 1)..segment.IndexOf(']')];
                if (int.TryParse(idxStr, out var arrayIdx))
                    current = current[arrayIdx];
            }
            else
            {
                current = current.GetProperty(segment);
            }
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString() ?? ""
            : current.GetRawText();
    }

    /// <summary>
    /// 依 ContentType 建立對應的 HttpContent。
    /// </summary>
    private static HttpContent BuildContent(string body, string? contentType)
    {
        var mediaType = string.IsNullOrWhiteSpace(contentType) ? "application/json" : contentType;

        if (mediaType == "application/x-www-form-urlencoded")
        {
            var pairs = ParseFormUrlEncoded(body);
            return new FormUrlEncodedContent(pairs);
        }

        if (mediaType == "multipart/form-data")
            return BuildMultipartContent(body);

        return new StringContent(body, System.Text.Encoding.UTF8, mediaType);
    }

    /// <summary>
    /// 組裝 multipart/form-data 內容。
    /// Body Template 為 JSON 格式的 parts 描述：
    /// <code>
    /// { "parts": [
    ///   { "name": "file", "filename": "report.csv", "contentType": "text/csv", "data": "..." },
    ///   { "name": "channel", "value": "#reports" }
    /// ]}
    /// </code>
    /// data 欄位支援：純文字（UTF-8 bytes）或 "base64:..." 前綴（base64 解碼為二進位）。
    /// </summary>
    private static HttpContent BuildMultipartContent(string body)
    {
        var multipart = new MultipartFormDataContent();

        try
        {
            using var doc = JsonDocument.Parse(body);
            var parts = doc.RootElement.GetProperty("parts");

            foreach (var part in parts.EnumerateArray())
            {
                var name = part.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "field" : "field";

                try
                {
                    // 檔案類型的 part（有 data 欄位）
                    if (part.TryGetProperty("data", out var dataElement))
                    {
                        var dataStr = dataElement.GetString() ?? "";
                        byte[] bytes;

                        if (dataStr.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
                            bytes = Convert.FromBase64String(dataStr[7..]);
                        else
                            bytes = System.Text.Encoding.UTF8.GetBytes(dataStr);

                        var byteContent = new ByteArrayContent(bytes);
                        var partContentType = part.TryGetProperty("contentType", out var ctEl)
                            ? ctEl.GetString() ?? "application/octet-stream"
                            : "application/octet-stream";
                        byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(partContentType);

                        var filename = part.TryGetProperty("filename", out var fnEl)
                            ? fnEl.GetString() ?? "file"
                            : "file";
                        multipart.Add(byteContent, name, filename);
                    }
                    // 純文字欄位（有 value 欄位）
                    else if (part.TryGetProperty("value", out var valueElement))
                    {
                        multipart.Add(new StringContent(valueElement.GetString() ?? ""), name);
                    }
                }
                catch
                {
                    // 跳過格式錯誤的 part（如 base64 無效），不影響其他 parts
                }
            }
        }
        catch (Exception ex)
        {
            // JSON 解析失敗 → 整個 body 作為單一 text part
            multipart.Add(new StringContent(body), "data");
            multipart.Add(new StringContent($"[Parse Warning] {ex.Message}"), "_warning");
        }

        return multipart;
    }

    /// <summary>
    /// 解析 form-urlencoded body — 支援 JSON object 或 key=value 格式。
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string>> ParseFormUrlEncoded(string body)
    {
        // 優先嘗試 JSON → key-value pairs
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(body,
                CaseInsensitiveOptions);
            if (dict is not null)
                return dict.Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value?.ToString() ?? ""));
        }
        catch
        {
            // fallback: 解析 key=value&key2=value2 格式
        }

        return body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var parts = pair.Split('=', 2);
                return new KeyValuePair<string, string>(
                    Uri.UnescapeDataString(parts[0].Trim()),
                    parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Trim()) : "");
            });
    }

    /// <summary>
    /// 從 URL 模板提取 {param} 名稱。
    /// </summary>
    private static List<string> ExtractParamNames(string urlTemplate) =>
        Regex.Matches(urlTemplate, @"\{(\w+)\}").Select(m => m.Groups[1].Value).ToList();
}
