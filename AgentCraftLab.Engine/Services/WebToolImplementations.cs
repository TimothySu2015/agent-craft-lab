using System.ComponentModel;
using System.Text.RegularExpressions;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// Web 類工具實作（URL Fetch）。
/// </summary>
internal static partial class ToolImplementations
{
    internal static async Task<string> UrlFetchAsync([Description("要抓取的完整 URL，例如 https://example.com")] string url, IHttpClientFactory httpFactory)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                return "Invalid URL. Please provide a valid http or https URL.";
            }

            // SSRF 防護
            var (isSafe, ssrfError) = await SafeUrlValidator.ValidateAsync(url);
            if (!isSafe) return $"[Blocked] {ssrfError}";

            using var http = httpFactory.CreateClient();
            http.DefaultRequestHeaders.Add("User-Agent", "AgentCraftLab/1.0");
            http.Timeout = TimeSpan.FromSeconds(Timeouts.DiscoverySeconds);
            http.MaxResponseContentBufferSize = 512 * 1024;

            var response = await http.GetAsync(uri);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("text") && !contentType.Contains("json") && !contentType.Contains("xml"))
            {
                return $"URL returned non-text content ({contentType}). Only text-based content is supported.";
            }

            var html = await response.Content.ReadAsStringAsync();
            var text = Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
            text = CleanHtml(text);
            text = Regex.Replace(text, @"\s+", " ").Trim();
            text = System.Net.WebUtility.HtmlDecode(text);
            text = StringUtils.Truncate(text, 3000, "... (truncated)");

            return string.IsNullOrWhiteSpace(text)
                ? "The page returned no readable text content."
                : $"Content from {url}:\n\n{text}";
        }
        catch (Exception ex)
        {
            return $"Failed to fetch URL: {ex.Message}";
        }
    }
}
