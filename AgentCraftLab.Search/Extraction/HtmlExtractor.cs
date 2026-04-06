using AgentCraftLab.Search.Abstractions;
using HtmlAgilityPack;

namespace AgentCraftLab.Search.Extraction;

/// <summary>
/// HTML 文字擷取器（使用 HtmlAgilityPack，去除標籤保留結構）。
/// </summary>
public class HtmlExtractor : IDocumentExtractor
{
    private static readonly HashSet<string> SupportedTypes =
        ["text/html", "application/xhtml+xml"];

    public bool CanExtract(string mimeType) =>
        SupportedTypes.Contains(mimeType);

    public Task<ExtractionResult> ExtractAsync(byte[] data, string fileName, CancellationToken ct = default)
    {
        var html = System.Text.Encoding.UTF8.GetString(data);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // 移除 script 和 style 節點
        var nodesToRemove = doc.DocumentNode.SelectNodes("//script|//style");
        if (nodesToRemove is not null)
        {
            foreach (var node in nodesToRemove)
            {
                node.Remove();
            }
        }

        var text = HtmlEntity.DeEntitize(doc.DocumentNode.InnerText);

        // 清理多餘空白行
        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);
        var cleanText = string.Join("\n", lines);

        var metadata = new Dictionary<string, string> { ["format"] = "HTML" };

        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        if (!string.IsNullOrWhiteSpace(titleNode?.InnerText))
        {
            metadata["title"] = HtmlEntity.DeEntitize(titleNode.InnerText).Trim();
        }

        var descNode = doc.DocumentNode.SelectSingleNode("//meta[@name='description']");
        var descContent = descNode?.GetAttributeValue("content", "");
        if (!string.IsNullOrWhiteSpace(descContent))
        {
            metadata["description"] = descContent;
        }

        return Task.FromResult(new ExtractionResult
        {
            Text = cleanText,
            FileName = fileName,
            MimeType = "text/html",
            Metadata = metadata
        });
    }
}
