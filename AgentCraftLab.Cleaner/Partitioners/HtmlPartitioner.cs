using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;
using HtmlAgilityPack;

namespace AgentCraftLab.Cleaner.Partitioners;

/// <summary>
/// HTML 分割器 — 利用 HTML 標籤直接對應元素類型。
/// </summary>
public sealed class HtmlPartitioner : IPartitioner
{
    private static readonly HashSet<string> SupportedMimeTypes =
        ["text/html", "application/xhtml+xml"];

    private static readonly HashSet<string> SkipTags =
        ["script", "style", "noscript", "svg", "head"];

    // 標籤 → 元素類型的直接映射
    private static readonly Dictionary<string, ElementType> TagToElementType = new()
    {
        ["h1"] = ElementType.Title, ["h2"] = ElementType.Title,
        ["h3"] = ElementType.Title, ["h4"] = ElementType.Title,
        ["h5"] = ElementType.Title, ["h6"] = ElementType.Title,
        ["li"] = ElementType.ListItem,
        ["pre"] = ElementType.CodeSnippet, ["code"] = ElementType.CodeSnippet,
        ["figcaption"] = ElementType.FigureCaption,
        ["header"] = ElementType.Header, ["footer"] = ElementType.Footer,
        ["address"] = ElementType.Address,
        ["p"] = ElementType.NarrativeText, ["blockquote"] = ElementType.NarrativeText,
    };

    // 容器標籤 — 遞迴子節點
    private static readonly HashSet<string> ContainerTags =
    [
        "div", "section", "article", "main", "aside", "nav",
        "ul", "ol", "dl", "form", "fieldset", "details", "summary", "span",
    ];

    public bool CanPartition(string mimeType) =>
        SupportedMimeTypes.Contains(mimeType.ToLowerInvariant());

    public Task<IReadOnlyList<DocumentElement>> PartitionAsync(
        byte[] data,
        string fileName,
        PartitionOptions? options = null,
        CancellationToken ct = default)
    {
        var html = System.Text.Encoding.UTF8.GetString(data);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var elements = new List<DocumentElement>();
        var index = 0;
        var docMetadata = ExtractDocMetadata(doc);

        var body = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
        TraverseNodes(body, fileName, elements, ref index, docMetadata, ct);

        return Task.FromResult<IReadOnlyList<DocumentElement>>(elements);
    }

    private static void TraverseNodes(
        HtmlNode parent, string fileName,
        List<DocumentElement> elements, ref int index,
        Dictionary<string, string> docMetadata, CancellationToken ct)
    {
        foreach (var node in parent.ChildNodes)
        {
            ct.ThrowIfCancellationRequested();

            if (node.NodeType == HtmlNodeType.Comment)
            {
                continue;
            }

            // 純文字節點
            if (node.NodeType == HtmlNodeType.Text)
            {
                AddTextElement(node, ElementType.NarrativeText, fileName, elements, ref index, docMetadata);
                continue;
            }

            var tag = node.Name.ToLowerInvariant();

            if (SkipTags.Contains(tag))
            {
                continue;
            }

            // 表格 — 特殊處理
            if (tag == "table")
            {
                AddTableElement(node, fileName, elements, ref index, docMetadata);
                continue;
            }

            // 圖片 — 取 alt text
            if (tag == "img")
            {
                var alt = node.GetAttributeValue("alt", "");
                if (!string.IsNullOrWhiteSpace(alt))
                {
                    elements.Add(PartitionerHelper.CreateElement(
                        ElementType.FigureCaption, alt, fileName, ref index, docMetadata));
                }
                continue;
            }

            // 已知標籤 → 直接映射
            if (TagToElementType.TryGetValue(tag, out var elementType))
            {
                AddElement(node, elementType, fileName, elements, ref index, docMetadata);
                continue;
            }

            // 容器標籤 → 遞迴
            if (ContainerTags.Contains(tag) || node.HasChildNodes)
            {
                TraverseNodes(node, fileName, elements, ref index, docMetadata, ct);
            }
        }
    }

    private static void AddElement(
        HtmlNode node, ElementType type, string fileName,
        List<DocumentElement> elements, ref int index,
        Dictionary<string, string> docMetadata)
    {
        var text = HtmlEntity.DeEntitize(node.InnerText).Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            elements.Add(PartitionerHelper.CreateElement(
                type, text, fileName, ref index, docMetadata));
        }
    }

    private static void AddTextElement(
        HtmlNode node, ElementType type, string fileName,
        List<DocumentElement> elements, ref int index,
        Dictionary<string, string> docMetadata)
    {
        var text = HtmlEntity.DeEntitize(node.InnerText).Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            elements.Add(PartitionerHelper.CreateElement(
                type, text, fileName, ref index, docMetadata));
        }
    }

    private static void AddTableElement(
        HtmlNode tableNode, string fileName,
        List<DocumentElement> elements, ref int index,
        Dictionary<string, string> docMetadata)
    {
        var rows = tableNode.SelectNodes(".//tr");
        if (rows is null || rows.Count == 0)
        {
            return;
        }

        var tableText = PartitionerHelper.ToMarkdownTable(
            rows.Select(row =>
            {
                var cells = row.SelectNodes(".//td|.//th");
                return cells?.Select(c => HtmlEntity.DeEntitize(c.InnerText))
                    ?? Enumerable.Empty<string>();
            }));

        if (!string.IsNullOrWhiteSpace(tableText))
        {
            elements.Add(PartitionerHelper.CreateElement(
                ElementType.Table, tableText, fileName, ref index, docMetadata));
        }
    }

    private static Dictionary<string, string> ExtractDocMetadata(HtmlDocument doc)
    {
        var docMetadata = new Dictionary<string, string> { [MetadataKeys.Format] = "HTML" };

        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        if (!string.IsNullOrWhiteSpace(titleNode?.InnerText))
        {
            docMetadata[MetadataKeys.Title] = HtmlEntity.DeEntitize(titleNode.InnerText).Trim();
        }

        var descNode = doc.DocumentNode.SelectSingleNode("//meta[@name='description']");
        var descContent = descNode?.GetAttributeValue("content", "");
        if (!string.IsNullOrWhiteSpace(descContent))
        {
            docMetadata[MetadataKeys.Description] = descContent;
        }

        return docMetadata;
    }
}
