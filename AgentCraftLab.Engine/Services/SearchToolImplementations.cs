using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 搜尋類工具實作（Azure Web Search, Tavily, Brave, DuckDuckGo, Wikipedia）。
/// </summary>
internal static partial class ToolImplementations
{
    private static readonly HashSet<string> ValidSerperTypes = ["search", "news", "images", "places"];
    // ── Shared Helpers ──

    private static string CleanHtml(string html)
        => Regex.Replace(html, @"<[^>]+>", " ")
            .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&quot;", "\"").Replace("&#39;", "'");

    private static List<string> ExtractSearchItems(
        JsonElement array, string titleProp, string urlProp, string snippetProp, int maxItems = 5)
    {
        var results = new List<string>();
        foreach (var item in array.EnumerateArray().Take(maxItems))
        {
            var title = item.TryGetProperty(titleProp, out var tp) ? tp.GetString() ?? "" : "";
            var url = item.TryGetProperty(urlProp, out var up) ? up.GetString() ?? "" : "";
            var snippet = item.TryGetProperty(snippetProp, out var sp) ? sp.GetString() ?? "" : "";
            results.Add($"**{title}**\n  URL: {url}\n  {StringUtils.Truncate(snippet, 200)}");
        }

        return results;
    }

    private static string FormatSearchResponse(string searchName, string query, List<string> results) =>
        results.Count > 0
            ? $"{searchName} search results for '{query}':\n\n{string.Join("\n\n", results)}"
            : $"No results found for '{query}'.";

    // ── Azure Web Search ──

    internal static async Task<string> AzureWebSearchAsync(string query, string? endpoint, string? apiKey, string? model, IHttpClientFactory httpFactory)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
        {
            return "Azure Web Search requires Azure OpenAI credentials. Please configure Endpoint and API Key in the Credentials panel.";
        }

        try
        {
            using var http = httpFactory.CreateClient();
            http.DefaultRequestHeaders.Add("api-key", apiKey);
            http.Timeout = TimeSpan.FromSeconds(Timeouts.ToolCallSeconds);

            var requestBody = JsonSerializer.Serialize(new
            {
                model = model ?? "gpt-4o",
                input = query,
                tools = new[] { new { type = "web_search_preview" } }
            });

            var url = $"{endpoint.TrimEnd('/')}/openai/v1/responses";
            var content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
            var response = await http.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return $"Azure Web Search failed ({response.StatusCode}): {StringUtils.Truncate(responseText, 300)}";
            }

            var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            string? resultText = null;

            if (root.TryGetProperty("output", out var output))
            {
                foreach (var item in output.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var t) && t.GetString() == "message" &&
                        item.TryGetProperty("content", out var contents))
                    {
                        foreach (var c in contents.EnumerateArray())
                        {
                            if (c.TryGetProperty("type", out var ct) && ct.GetString() == "output_text" &&
                                c.TryGetProperty("text", out var txt))
                            {
                                resultText = (resultText ?? "") + txt.GetString();
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(resultText) && root.TryGetProperty("output_text", out var outputText) &&
                outputText.ValueKind == JsonValueKind.String)
            {
                resultText = outputText.GetString();
            }

            return !string.IsNullOrWhiteSpace(resultText)
                ? $"Web search results:\n\n{resultText}"
                : $"No results found for '{query}'.";
        }
        catch (Exception ex)
        {
            return $"Azure Web Search failed: {ex.Message}";
        }
    }

    // ── Tavily Search ──

    internal static async Task<string> TavilySearchAsync(string query, string? apiKey, IHttpClientFactory httpFactory)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return "Tavily Search requires an API key. Get one free at https://tavily.com";
        }

        try
        {
            using var http = httpFactory.CreateClient();
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            http.Timeout = TimeSpan.FromSeconds(Timeouts.HttpApiSeconds);

            var requestBody = JsonSerializer.Serialize(new { query, max_results = 5, include_answer = "basic" });
            var content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
            var response = await http.PostAsync("https://api.tavily.com/search", content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return $"Tavily search failed ({response.StatusCode}): {StringUtils.Truncate(responseText, 200)}";
            }

            var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            var results = new List<string>();

            if (root.TryGetProperty("answer", out var answer) && !string.IsNullOrWhiteSpace(answer.GetString()))
            {
                results.Add($"**Summary:** {answer.GetString()}");
            }

            if (root.TryGetProperty("results", out var items))
            {
                results.AddRange(ExtractSearchItems(items, "title", "url", "content"));
            }

            return FormatSearchResponse("Tavily", query, results);
        }
        catch (Exception ex)
        {
            return $"Tavily search failed: {ex.Message}";
        }
    }

    // ── Tavily Extract ──

    internal static async Task<string> TavilyExtractAsync(string urls, string? apiKey, IHttpClientFactory httpFactory)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return "Tavily Extract requires an API key. Get one free at https://tavily.com";
        }

        if (string.IsNullOrWhiteSpace(urls))
        {
            return "No URLs provided.";
        }

        try
        {
            // urls 可以是 JSON array 或逗號/換行分隔
            List<string> urlList;
            if (urls.TrimStart().StartsWith('['))
            {
                urlList = JsonSerializer.Deserialize<List<string>>(urls) ?? [];
            }
            else
            {
                urlList = urls.Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }

            if (urlList.Count == 0)
            {
                return "No valid URLs provided.";
            }

            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(Timeouts.HttpApiSeconds);

            var requestBody = JsonSerializer.Serialize(new { api_key = apiKey, urls = urlList });
            var content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
            var response = await http.PostAsync("https://api.tavily.com/extract", content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return $"Tavily extract failed ({response.StatusCode}): {StringUtils.Truncate(responseText, 200)}";
            }

            var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            var results = new List<string>();

            if (root.TryGetProperty("results", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    var url = item.TryGetProperty("url", out var u) ? u.GetString() : "";
                    var rawContent = item.TryGetProperty("raw_content", out var rc) ? rc.GetString() : "";
                    if (string.IsNullOrWhiteSpace(rawContent) && item.TryGetProperty("content", out var c))
                    {
                        rawContent = c.GetString();
                    }

                    if (!string.IsNullOrWhiteSpace(rawContent))
                    {
                        results.Add($"**URL:** {url}\n{StringUtils.Truncate(rawContent!, 2000)}");
                    }
                }
            }

            // 失敗的 URL
            if (root.TryGetProperty("failed_results", out var failed))
            {
                foreach (var item in failed.EnumerateArray())
                {
                    var url = item.TryGetProperty("url", out var u) ? u.GetString() : "";
                    var error = item.TryGetProperty("error", out var e) ? e.GetString() : "unknown";
                    results.Add($"**FAILED:** {url} — {error}");
                }
            }

            return results.Count > 0
                ? $"## Tavily Extract ({urlList.Count} URLs)\n\n{string.Join("\n\n---\n\n", results)}"
                : "No content extracted.";
        }
        catch (Exception ex)
        {
            return $"Tavily extract failed: {ex.Message}";
        }
    }

    // ── Brave Search ──

    internal static async Task<string> BraveSearchAsync(string query, string? apiKey, IHttpClientFactory httpFactory)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return "Brave Search requires an API key. Get one free at https://brave.com/search/api/";
        }

        try
        {
            using var http = httpFactory.CreateClient();
            http.DefaultRequestHeaders.Add("X-Subscription-Token", apiKey);
            http.DefaultRequestHeaders.Add("Accept", "application/json");
            http.Timeout = TimeSpan.FromSeconds(Timeouts.DiscoverySeconds);

            var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count=5";
            var response = await http.GetAsync(url);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return $"Brave search failed ({response.StatusCode}): {StringUtils.Truncate(responseText, 200)}";
            }

            var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            var results = new List<string>();

            if (root.TryGetProperty("web", out var web) && web.TryGetProperty("results", out var items))
            {
                results.AddRange(ExtractSearchItems(items, "title", "url", "description"));
            }

            return FormatSearchResponse("Brave", query, results);
        }
        catch (Exception ex)
        {
            return $"Brave search failed: {ex.Message}";
        }
    }

    // ── Serper (Google Search API) ──

    internal static async Task<string> SerperSearchAsync(string query, string? apiKey, IHttpClientFactory httpFactory, string type = "search", int num = 10)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return "Serper Search requires an API key. Get one free at https://serper.dev";
        }

        if (!ValidSerperTypes.Contains(type))
        {
            type = "search";
        }

        try
        {
            using var http = httpFactory.CreateClient();
            http.DefaultRequestHeaders.Add("X-API-KEY", apiKey);
            http.Timeout = TimeSpan.FromSeconds(Timeouts.HttpApiSeconds);

            var requestBody = JsonSerializer.Serialize(new { q = query, num });
            var content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
            var response = await http.PostAsync($"https://google.serper.dev/{type}", content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return $"Serper {type} failed ({response.StatusCode}): {StringUtils.Truncate(responseText, 200)}";
            }

            var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            var results = new List<string>();

            if (type == "news")
            {
                // News Results: title, link, snippet, date, source
                if (root.TryGetProperty("news", out var news))
                {
                    foreach (var item in news.EnumerateArray())
                    {
                        var title = item.TryGetProperty("title", out var t) ? t.GetString() : "";
                        var link = item.TryGetProperty("link", out var l) ? l.GetString() : "";
                        var snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() : "";
                        var date = item.TryGetProperty("date", out var d) ? d.GetString() : "";
                        var source = item.TryGetProperty("source", out var src) ? src.GetString() : "";
                        results.Add($"[{source} {date}] **{title}**\n{link}\n{snippet}");
                    }
                }
            }
            else
            {
                // Answer Box（search 模式）
                if (root.TryGetProperty("answerBox", out var answerBox))
                {
                    var answer = answerBox.TryGetProperty("answer", out var a) ? a.GetString()
                        : answerBox.TryGetProperty("snippet", out var s) ? s.GetString()
                        : null;
                    if (!string.IsNullOrWhiteSpace(answer))
                    {
                        results.Add($"**Answer:** {answer}");
                    }
                }

                // Knowledge Graph（search 模式）
                if (root.TryGetProperty("knowledgeGraph", out var kg))
                {
                    var kgTitle = kg.TryGetProperty("title", out var kt) ? kt.GetString() : null;
                    var kgDesc = kg.TryGetProperty("description", out var kd) ? kd.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(kgTitle))
                    {
                        results.Add($"**{kgTitle}**: {kgDesc ?? ""}");
                    }
                }

                // Organic Results（search 模式）
                if (root.TryGetProperty("organic", out var organic))
                {
                    results.AddRange(ExtractSearchItems(organic, "title", "link", "snippet"));
                }
            }

            return FormatSearchResponse($"Serper ({type})", query, results);
        }
        catch (Exception ex)
        {
            return $"Serper {type} failed: {ex.Message}";
        }
    }

    // ── Web Search (Free - DuckDuckGo + Wikipedia fallback) ──

    internal static async Task<string> WebSearchAsync([Description("搜尋查詢關鍵字")] string query, IHttpClientFactory httpFactory)
    {
        using var http = httpFactory.CreateClient();
        http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        http.Timeout = TimeSpan.FromSeconds(Timeouts.SearchSeconds);

        try
        {
            var ddgUrl = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1&skip_disambig=1";
            var ddgResponse = await http.GetStringAsync(ddgUrl);
            var ddgDoc = JsonDocument.Parse(ddgResponse);
            var ddgRoot = ddgDoc.RootElement;

            var results = new List<string>();

            var abstractText = ddgRoot.GetProperty("AbstractText").GetString();
            if (!string.IsNullOrWhiteSpace(abstractText))
            {
                var source = ddgRoot.GetProperty("AbstractSource").GetString();
                var abstractUrl = ddgRoot.GetProperty("AbstractURL").GetString();
                results.Add($"**{source}**: {abstractText}\n  URL: {abstractUrl}");
            }

            var answerText = ddgRoot.GetProperty("Answer").GetString();
            if (!string.IsNullOrWhiteSpace(answerText))
            {
                results.Add($"**Direct Answer**: {answerText}");
            }

            if (ddgRoot.TryGetProperty("RelatedTopics", out var topics))
            {
                foreach (var topic in topics.EnumerateArray().Take(5))
                {
                    if (topic.TryGetProperty("Text", out var text) &&
                        topic.TryGetProperty("FirstURL", out var topicUrl))
                    {
                        var t = text.GetString();
                        var u = topicUrl.GetString();
                        if (!string.IsNullOrWhiteSpace(t))
                        {
                            results.Add($"- {t}\n  URL: {u}");
                        }
                    }
                }
            }

            if (results.Count > 0)
            {
                return $"Search results for '{query}':\n\n{string.Join("\n\n", results)}";
            }
        }
        catch (Exception ex) { _ = ex; /* DuckDuckGo unavailable, fall back to Wikipedia */ }

        return await WikipediaSearchAsync(query, httpFactory);
    }

    // ── Wikipedia ──

    internal static async Task<string> WikipediaSearchAsync([Description("搜尋查詢關鍵字")] string query, IHttpClientFactory httpFactory)
    {
        try
        {
            using var http = httpFactory.CreateClient();
            http.DefaultRequestHeaders.Add("User-Agent", "AgentCraftLab/1.0");
            http.Timeout = TimeSpan.FromSeconds(Timeouts.DiscoverySeconds);

            var hasChineseChars = Regex.IsMatch(query, @"[\u4e00-\u9fff]");
            var wikiLang = hasChineseChars ? "zh" : "en";

            var wikiSearchUrl = $"https://{wikiLang}.wikipedia.org/w/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(query)}&srlimit=5&format=json&utf8=1";
            var wikiResponse = await http.GetStringAsync(wikiSearchUrl);
            var wikiDoc = JsonDocument.Parse(wikiResponse);

            var searchResults = wikiDoc.RootElement.GetProperty("query").GetProperty("search");
            var results = new List<string>();
            foreach (var item in searchResults.EnumerateArray().Take(5))
            {
                var title = item.GetProperty("title").GetString() ?? "";
                var snippet = item.GetProperty("snippet").GetString() ?? "";
                snippet = CleanHtml(snippet).Trim();

                if (!string.IsNullOrWhiteSpace(title))
                {
                    var pageUrl = $"https://{wikiLang}.wikipedia.org/wiki/{Uri.EscapeDataString(title.Replace(' ', '_'))}";
                    results.Add($"**{title}**\n  URL: {pageUrl}\n  {snippet}");
                }
            }

            if (results.Count > 0)
            {
                return $"Wikipedia results for '{query}':\n\n{string.Join("\n\n", results)}";
            }
        }
        catch (Exception ex)
        {
            return $"Wikipedia search failed: {ex.Message}";
        }

        return $"No Wikipedia results found for '{query}'.";
    }
}
