using System.Collections.Concurrent;
using System.ComponentModel;
using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 工具註冊表服務：管理所有可供 Agent 使用的內建工具。
/// 新增工具只需在對應分類方法中加一行 Register(...)。
/// </summary>
public class ToolRegistryService : IToolRegistry
{
    private readonly ConcurrentDictionary<string, ToolDefinition> _registry = new();
    private readonly IHttpClientFactory _httpFactory;

    public ToolRegistryService(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
        RegisterSearchTools();
        RegisterUtilityTools();
        RegisterWebTools();
        RegisterDataTools();
        RegisterCodeExplorerTools();
    }

    private void RegisterSearchTools()
    {
        Register("azure_web_search", "Azure Web Search", "透過 Azure OpenAI Responses API 搜尋即時網路資訊（需要 Azure OpenAI 憑證）",
            () => AIFunctionFactory.Create(
                ([Description("搜尋查詢關鍵字")] string query) => ToolImplementations.AzureWebSearchAsync(query, null, null, null, _httpFactory),
                name: "AzureWebSearch", description: "透過 Azure OpenAI Web Search 搜尋即時網路資訊（需要憑證）"),
            ToolCategory.Search, "&#x1F310;", "azure-openai",
            credentialFactory: creds =>
            {
                var aoai = creds["azure-openai"];
                var model = string.IsNullOrWhiteSpace(aoai.Model) ? "gpt-4o" : aoai.Model;
                return AIFunctionFactory.Create(
                    ([Description("搜尋查詢關鍵字")] string query) => ToolImplementations.AzureWebSearchAsync(query, aoai.Endpoint, aoai.ApiKey, model, _httpFactory),
                    name: "AzureWebSearch",
                    description: "透過 Azure OpenAI Web Search 搜尋即時網路資訊");
            });

        Register("tavily_search", "Tavily Search", "AI 專用搜尋引擎，回傳整理過的結果與摘要（免費 1000 次/月）",
            () => AIFunctionFactory.Create(
                ([Description("搜尋查詢關鍵字")] string query) => ToolImplementations.TavilySearchAsync(query, null, _httpFactory),
                name: "TavilySearch", description: "透過 Tavily AI 搜尋引擎搜尋即時網路資訊（需要憑證）"),
            ToolCategory.Search, "&#x1F9E0;", "tavily",
            credentialFactory: creds =>
            {
                var key = creds["tavily"].ApiKey;
                return AIFunctionFactory.Create(
                    ([Description("搜尋查詢關鍵字")] string query) => ToolImplementations.TavilySearchAsync(query, key, _httpFactory),
                    name: "TavilySearch",
                    description: "透過 Tavily AI 搜尋引擎搜尋即時網路資訊");
            });

        Register("tavily_extract", "Tavily Extract", "從 URL 提取純淨網頁內容（自動去除廣告/導覽列），支援批次提取多個 URL",
            () => AIFunctionFactory.Create(
                ([Description("要提取內容的 URL，可以是 JSON array 或逗號分隔的多個 URL")] string urls) => ToolImplementations.TavilyExtractAsync(urls, null, _httpFactory),
                name: "TavilyExtract", description: "從 URL 提取純淨網頁內容，自動去除廣告和導覽列（需要憑證）"),
            ToolCategory.Search, "&#x1F4C4;", "tavily",
            credentialFactory: creds =>
            {
                var key = creds["tavily"].ApiKey;
                return AIFunctionFactory.Create(
                    ([Description("要提取內容的 URL，可以是 JSON array 或逗號分隔的多個 URL")] string urls) => ToolImplementations.TavilyExtractAsync(urls, key, _httpFactory),
                    name: "TavilyExtract",
                    description: "從 URL 提取純淨網頁內容，自動去除廣告和導覽列，支援批次多個 URL");
            });

        Register("brave_search", "Brave Search", "隱私導向搜尋引擎（免費 2000 次/月）",
            () => AIFunctionFactory.Create(
                ([Description("搜尋查詢關鍵字")] string query) => ToolImplementations.BraveSearchAsync(query, null, _httpFactory),
                name: "BraveSearch", description: "透過 Brave Search 搜尋即時網路資訊（需要憑證）"),
            ToolCategory.Search, "&#x1F981;", "brave",
            credentialFactory: creds =>
            {
                var key = creds["brave"].ApiKey;
                return AIFunctionFactory.Create(
                    ([Description("搜尋查詢關鍵字")] string query) => ToolImplementations.BraveSearchAsync(query, key, _httpFactory),
                    name: "BraveSearch",
                    description: "透過 Brave Search 搜尋即時網路資訊");
            });

        Register("serper_search", "Serper (Google)", "透過 Serper.dev 呼叫 Google Search API，支援 search/news/images/places 四種模式（免費 2500 次）",
            () => AIFunctionFactory.Create(
                ([Description("搜尋查詢關鍵字")] string query,
                 [Description("搜尋類型：search（網頁）、news（新聞）、images（圖片）、places（地點），預設 search")] string type = "search",
                 [Description("回傳筆數，預設 10")] int num = 10) => ToolImplementations.SerperSearchAsync(query, null, _httpFactory, type, num),
                name: "SerperSearch", description: "透過 Serper.dev Google Search API 搜尋網路資訊（需要憑證）"),
            ToolCategory.Search, "&#x1F310;", "serper",
            credentialFactory: creds =>
            {
                var key = creds["serper"].ApiKey;
                return AIFunctionFactory.Create(
                    ([Description("搜尋查詢關鍵字")] string query,
                     [Description("搜尋類型：search（網頁）、news（新聞）、images（圖片）、places（地點），預設 search")] string type = "search",
                     [Description("回傳筆數，預設 10")] int num = 10) => ToolImplementations.SerperSearchAsync(query, key, _httpFactory, type, num),
                    name: "SerperSearch",
                    description: "透過 Serper.dev Google Search API 搜尋網路資訊，支援 search/news/images/places 四種模式");
            });

        Register("web_search", "Web Search (Free)", "透過 DuckDuckGo + Wikipedia 搜尋資訊（免費、無需 API Key）",
            () => AIFunctionFactory.Create(
                ([Description("搜尋查詢關鍵字")] string query) => ToolImplementations.WebSearchAsync(query, _httpFactory),
                name: "WebSearch", description: "透過 DuckDuckGo + Wikipedia 搜尋資訊（免費、無需 API Key）"),
            ToolCategory.Search, "&#x1F50D;");

        Register("wikipedia", "Wikipedia", "搜尋 Wikipedia 百科全書（自動偵測中/英文）",
            () => AIFunctionFactory.Create(
                ([Description("搜尋查詢關鍵字")] string query) => ToolImplementations.WikipediaSearchAsync(query, _httpFactory),
                name: "Wikipedia", description: "搜尋 Wikipedia 百科全書（自動偵測中/英文）"),
            ToolCategory.Search, "&#x1F4DA;");
    }

    private void RegisterUtilityTools()
    {
        Register("get_datetime", "Date & Time", "取得目前的日期、時間與時區資訊",
            () => AIFunctionFactory.Create(ToolImplementations.GetCurrentDateTime, name: "GetDateTime", description: "取得目前的日期、時間與時區資訊"),
            ToolCategory.Utility, "&#x1F552;");

        Register("calculator", "Calculator", "計算數學表達式（加減乘除、括號）",
            () => AIFunctionFactory.Create(ToolImplementations.Calculate, name: "Calculator", description: "計算數學表達式（加減乘除、括號）"),
            ToolCategory.Utility, "&#x1F522;");

        Register("uuid_generator", "UUID Generator", "產生唯一的 UUID / GUID",
            () => AIFunctionFactory.Create(ToolImplementations.GenerateUuid, name: "UuidGenerator", description: "產生唯一的 UUID / GUID"),
            ToolCategory.Utility, "&#x1F3B2;");
    }

    private void RegisterWebTools()
    {
        Register("url_fetch", "URL Fetch", "抓取指定網頁的文字內容摘要",
            () => AIFunctionFactory.Create(
                ([Description("要抓取的完整 URL，例如 https://example.com")] string url) => ToolImplementations.UrlFetchAsync(url, _httpFactory),
                name: "UrlFetch", description: "抓取指定網頁的文字內容摘要"),
            ToolCategory.Web, "&#x1F517;");
    }

    private void RegisterDataTools()
    {
        Register("json_parser", "JSON Parser", "解析 JSON 字串並提取指定欄位",
            () => AIFunctionFactory.Create(ToolImplementations.JsonParse, name: "JsonParser", description: "解析 JSON 字串並提取指定欄位"),
            ToolCategory.Data, "&#x1F4CB;");

        Register("csv_log_analyzer", "CSV Log Analyzer", "讀取指定目錄下所有 CSV 檔案，合併內容供 AI 分析 Log（支援檔名篩選、列數限制、子目錄遞迴）",
            () => AIFunctionFactory.Create(ToolImplementations.ReadCsvLogs, name: "CsvLogAnalyzer", description: "讀取指定目錄下所有 CSV 檔案，合併內容供 AI 分析 Log"),
            ToolCategory.Data, "&#x1F4CA;");

        Register("zip_extractor", "ZIP Extractor", "解壓縮 ZIP 檔案到暫存目錄，回傳解壓後的目錄路徑與檔案清單",
            () => AIFunctionFactory.Create(ToolImplementations.ExtractZip, name: "ZipExtractor", description: "解壓縮 ZIP 檔案到暫存目錄"),
            ToolCategory.Data, "&#x1F4E6;");

        Register("write_file", "Write File", "將文字內容寫入檔案（支援 .csv, .json, .txt, .md, .xml, .log, .yaml, .html, .tsv）",
            () => AIFunctionFactory.Create(ToolImplementations.WriteFile, name: "WriteFile", description: "將文字內容寫入檔案"),
            ToolCategory.Data, "&#x1F4DD;");

        Register("write_csv", "Write CSV", "將 JSON 陣列資料寫入 CSV 檔案（自動產生標頭、處理逗號/引號/換行的 RFC 4180 escaping）",
            () => AIFunctionFactory.Create(ToolImplementations.WriteCsv, name: "WriteCsv", description: "將 JSON 陣列資料寫入 CSV 檔案"),
            ToolCategory.Data, "&#x1F4C4;");

        Register("send_email", "Send Email", "透過 SMTP 發送電子郵件，支援附件（需要 SMTP 憑證）",
            () => AIFunctionFactory.Create(
                ([Description("收件人 Email（多人以逗號分隔）")] string to,
                 [Description("郵件主旨")] string subject,
                 [Description("郵件內容")] string body,
                 [Description("副本收件人（多人以逗號分隔），可留空")] string cc,
                 [Description("是否為 HTML 格式")] bool isHtml,
                 [Description("附件檔案路徑（多個以逗號分隔），或目錄路徑（會附加目錄下所有檔案），可留空")] string attachments) =>
                    Task.FromResult("Error: SMTP credentials not configured. Please set SMTP Host, From Email and Password in Settings."),
                name: "SendEmail", description: "透過 SMTP 發送電子郵件，支援附件（需要憑證）"),
            ToolCategory.Utility, "&#x2709;", "smtp",
            credentialFactory: creds =>
            {
                var smtp = creds["smtp"];
                var parts = smtp.Endpoint.Split(':', 2);
                var host = parts[0];
                var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 587;
                var from = smtp.Model;
                var pass = smtp.ApiKey;
                return AIFunctionFactory.Create(
                    ([Description("收件人 Email（多人以逗號分隔）")] string to,
                     [Description("郵件主旨")] string subject,
                     [Description("郵件內容")] string body,
                     [Description("副本收件人（多人以逗號分隔），可留空")] string cc,
                     [Description("是否為 HTML 格式")] bool isHtml,
                     [Description("附件檔案路徑（多個以逗號分隔），或目錄路徑（會附加目錄下所有檔案），可留空")] string attachments) =>
                        ToolImplementations.SendEmailAsync(to, subject, body, host, port, from, pass, cc, isHtml, attachments),
                    name: "SendEmail",
                    description: "透過 SMTP 發送電子郵件，支援附件");
            });
    }

    private void RegisterCodeExplorerTools()
    {
        Register("list_directory", "List Directory", "列出目錄結構（tree 格式），探索 codebase 的檔案組織",
            () => AIFunctionFactory.Create(ToolImplementations.ListDirectory,
                name: "ListDirectory", description: "列出目錄結構（tree 格式）"),
            ToolCategory.Data, "&#x1F4C2;");

        Register("read_file", "Read File", "讀取檔案指定行範圍（帶行號），檢視原始碼內容",
            () => AIFunctionFactory.Create(ToolImplementations.ReadFile,
                name: "ReadFile", description: "讀取檔案指定行範圍（帶行號）"),
            ToolCategory.Data, "&#x1F4C4;");

        Register("search_code", "Search Code", "在 codebase 中搜尋匹配 regex pattern 的程式碼（支援 context 行、檔案篩選）",
            () => AIFunctionFactory.Create(ToolImplementations.SearchCode,
                name: "SearchCode", description: "在 codebase 中搜尋匹配 regex pattern 的程式碼"),
            ToolCategory.Data, "&#x1F50E;");

        Register("file_diff", "File Diff", "比較兩個檔案的差異（unified diff 格式），支援忽略空白",
            () => AIFunctionFactory.Create(ToolImplementations.FileDiff,
                name: "FileDiff", description: "比較兩個檔案的差異，以 unified diff 格式輸出"),
            ToolCategory.Data, "&#x1F4CA;");

        Register("text_diff", "Text Diff", "比較兩段文字的差異（unified diff 格式），支援忽略空白",
            () => AIFunctionFactory.Create(ToolImplementations.TextDiff,
                name: "TextDiff", description: "比較兩段文字的差異，以 unified diff 格式輸出"),
            ToolCategory.Data, "&#x1F4DD;");
    }

    public void Register(string id, string displayName, string description,
        Func<AITool> factory, ToolCategory category, string icon = "&#x1F527;", string? requiredCredential = null,
        Func<Dictionary<string, ProviderCredential>, AITool>? credentialFactory = null)
    {
        _registry[id] = new ToolDefinition(id, displayName, description, factory, category, icon, requiredCredential, credentialFactory);
    }

    /// <summary>
    /// 根據工具 ID 清單解析出對應的 AITool 實例。credentials 用於需要憑證的工具。
    /// </summary>
    public IList<AITool> Resolve(List<string> toolIds, Dictionary<string, ProviderCredential>? credentials = null)
    {
        return toolIds
            .Where(_registry.ContainsKey)
            .Select(id =>
            {
                var def = _registry[id];
                if (def.CredentialFactory != null && credentials != null &&
                    def.RequiredCredential != null &&
                    credentials.TryGetValue(def.RequiredCredential, out var cred) &&
                    (!string.IsNullOrWhiteSpace(cred.ApiKey) || !string.IsNullOrWhiteSpace(cred.Endpoint)))
                {
                    return def.CredentialFactory(credentials);
                }
                return def.Factory();
            })
            .ToList();
    }

    /// <summary>
    /// 檢查工具是否不需要憑證、或已有可用憑證。用於 Skill 自動帶工具時過濾。
    /// </summary>
    public bool IsToolAvailable(string toolId, Dictionary<string, ProviderCredential>? credentials)
    {
        if (!_registry.TryGetValue(toolId, out var def))
        {
            return false;
        }

        // 不需要憑證的工具永遠可用
        if (def.RequiredCredential is null)
        {
            return true;
        }

        // 需要憑證：檢查是否已設定
        return credentials != null &&
               credentials.TryGetValue(def.RequiredCredential, out var cred) &&
               (!string.IsNullOrWhiteSpace(cred.ApiKey) || !string.IsNullOrWhiteSpace(cred.Endpoint));
    }

    public IReadOnlyList<ToolDefinition> GetAvailableTools()
        => _registry.Values.ToList();

    /// <summary>
    /// 取得所有需要憑證的工具 credential 類型（供憑證管理頁面動態顯示）。
    /// 回傳去重後的 (provider, displayName, icon) 清單。
    /// </summary>
    public IReadOnlyList<ToolCredentialType> GetToolCredentialTypes()
        => _registry.Values
            .Where(t => t.RequiredCredential is not null)
            .GroupBy(t => t.RequiredCredential!)
            .Select(g =>
            {
                var first = g.First();
                var toolNames = string.Join(", ", g.Select(t => t.DisplayName));
                return new ToolCredentialType(first.RequiredCredential!, toolNames, first.Icon);
            })
            .OrderBy(t => t.Provider)
            .ToList();

    /// <summary>
    /// 按分類取得所有工具（供 UI 分組顯示）。
    /// </summary>
    public IReadOnlyDictionary<ToolCategory, List<ToolDefinition>> GetByCategory()
        => _registry.Values
            .GroupBy(t => t.Category)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.DisplayName).ToList());

    /// <summary>
    /// 測試單一工具：解析工具、呼叫、回傳結果。
    /// </summary>
    public async Task<(bool Success, string Result)> TestToolAsync(string toolId, string input, Dictionary<string, ProviderCredential>? credentials = null)
    {
        try
        {
            var tools = Resolve([toolId], credentials);
            if (tools.Count == 0)
                return (false, $"Tool '{toolId}' not found or credentials missing.");

            var tool = tools[0];
            if (tool is AIFunction func)
            {
                // 根據工具類型決定參數：有 input 的傳 query/expression/etc，無 input 的傳空
                var dict = new Dictionary<string, object?>();
                if (!string.IsNullOrWhiteSpace(input))
                {
                    if (toolId == "send_email")
                    {
                        dict["to"] = input;
                        dict["subject"] = "Test Email from AgentCraftLab";
                        dict["body"] = "This is a test email sent from AgentCraftLab Studio.";
                        dict["cc"] = "";
                        dict["isHtml"] = false;
                        dict["attachments"] = "";
                    }
                    else
                    {
                        var paramName = toolId switch
                        {
                            "calculator" => "expression",
                            "url_fetch" => "url",
                            "json_parser" => "jsonString",
                            "csv_log_analyzer" => "directoryPath",
                            "list_directory" => "directoryPath",
                            "read_file" => "filePath",
                            "search_code" => "pattern",
                            "file_diff" => "filePath1",
                            "text_diff" => "text1",
                            "zip_extractor" => "zipFilePath",
                            "write_file" => "fileName",
                            "write_csv" => "fileName",
                            "ocr_recognize" => "imagePath",
                            "script_execute" => "code",
                            _ => "query"
                        };
                        dict[paramName] = input;
                        if (toolId == "write_file")
                        {
                            dict["content"] = "Test content from AgentCraftLab";
                        }
                        else if (toolId == "write_csv")
                        {
                            dict["jsonData"] = "[{\"Name\":\"Alice\",\"Score\":95},{\"Name\":\"Bob\",\"Score\":87}]";
                        }
                    }
                }

                var result = await func.InvokeAsync(new AIFunctionArguments(dict));
                var text = result?.ToString() ?? "(no output)";
                return (true, text);
            }

            return (false, "Tool is not an AIFunction.");
        }
        catch (Exception ex)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// 取得工具的預設測試輸入值。
    /// </summary>
    public static string GetDefaultTestInput(string toolId) => toolId switch
    {
        "azure_web_search" => "latest news today",
        "tavily_search" => "latest AI news",
        "tavily_extract" => "https://example.com",
        "brave_search" => "latest tech news",
        "serper_search" => "latest AI news",
        "web_search" => "Taiwan",
        "wikipedia" => "台灣",
        "get_datetime" => "",
        "calculator" => "2+3*4",
        "uuid_generator" => "",
        "url_fetch" => "https://example.com",
        "json_parser" => "{\"name\":\"test\",\"value\":42}|name",
        "csv_log_analyzer" => "C:\\Logs",
        "zip_extractor" => "C:\\test.zip",
        "write_file" => "test.txt",
        "write_csv" => "test.csv",
        "send_email" => "test@example.com",
        "list_directory" => ".",
        "read_file" => "Program.cs",
        "search_code" => "TODO",
        "file_diff" => "file1.txt",
        "text_diff" => "hello world",
        "ocr_recognize" => "test.png",
        "script_execute" => "result = input.toUpperCase()",
        _ => "test"
    };
}
