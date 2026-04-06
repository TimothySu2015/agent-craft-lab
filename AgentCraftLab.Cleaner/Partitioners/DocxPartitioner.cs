using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AgentCraftLab.Cleaner.Partitioners;

/// <summary>
/// Word (.docx) 分割器 — 將 DOCX 拆解為帶類型的 DocumentElement 序列。
/// 利用 OpenXml 的段落樣式（Heading / ListParagraph / Table）判斷元素類型。
/// </summary>
public sealed class DocxPartitioner : IPartitioner
{
    private const string SupportedMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public bool CanPartition(string mimeType) =>
        mimeType.Equals(SupportedMimeType, StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<DocumentElement>> PartitionAsync(
        byte[] data,
        string fileName,
        PartitionOptions? options = null,
        CancellationToken ct = default)
    {
        using var stream = new MemoryStream(data);
        using var doc = WordprocessingDocument.Open(stream, false)!;

        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return Task.FromResult<IReadOnlyList<DocumentElement>>([]);
        }

        var elements = new List<DocumentElement>();
        var index = 0;
        var docMetadata = ExtractDocMetadata(doc);

        foreach (var child in body.ChildElements)
        {
            ct.ThrowIfCancellationRequested();

            switch (child)
            {
                case Table table:
                    var tableText = ExtractTableText(table);
                    if (!string.IsNullOrWhiteSpace(tableText))
                    {
                        elements.Add(PartitionerHelper.CreateElement(
                            ElementType.Table, tableText, fileName, ref index, docMetadata));
                    }
                    break;

                case Paragraph paragraph:
                    var text = paragraph.InnerText;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    elements.Add(PartitionerHelper.CreateElement(
                        ClassifyParagraph(paragraph), text, fileName, ref index, docMetadata));
                    break;

                case SdtBlock sdtBlock:
                    var sdtText = sdtBlock.InnerText;
                    if (!string.IsNullOrWhiteSpace(sdtText))
                    {
                        elements.Add(PartitionerHelper.CreateElement(
                            ElementType.NarrativeText, sdtText, fileName, ref index, docMetadata));
                    }
                    break;
            }
        }

        if (options?.IncludeHeaderFooter != false)
        {
            ExtractHeaderFooter(doc, fileName, elements, ref index, docMetadata);
        }

        return Task.FromResult<IReadOnlyList<DocumentElement>>(elements);
    }

    /// <summary>依段落樣式判斷元素類型</summary>
    private static ElementType ClassifyParagraph(Paragraph paragraph)
    {
        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

        if (styleId is not null)
        {
            if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) ||
                styleId.Equals("Title", StringComparison.OrdinalIgnoreCase) ||
                styleId.Equals("Subtitle", StringComparison.OrdinalIgnoreCase))
            {
                return ElementType.Title;
            }

            if (styleId.Equals("ListParagraph", StringComparison.OrdinalIgnoreCase) ||
                styleId.StartsWith("List", StringComparison.OrdinalIgnoreCase) ||
                styleId.Contains("Bullet", StringComparison.OrdinalIgnoreCase))
            {
                return ElementType.ListItem;
            }

            if (styleId.Contains("Code", StringComparison.OrdinalIgnoreCase))
            {
                return ElementType.CodeSnippet;
            }
        }

        if (paragraph.ParagraphProperties?.NumberingProperties is not null)
        {
            return ElementType.ListItem;
        }

        return ElementType.NarrativeText;
    }

    private static string ExtractTableText(Table table)
    {
        var rows = table.Elements<TableRow>().ToList();
        if (rows.Count == 0)
        {
            return "";
        }

        return PartitionerHelper.ToMarkdownTable(
            rows.Select(row => row.Elements<TableCell>().Select(c => c.InnerText)));
    }

    private static void ExtractHeaderFooter(
        WordprocessingDocument doc, string fileName,
        List<DocumentElement> elements, ref int index,
        Dictionary<string, string> docMetadata)
    {
        var mainPart = doc.MainDocumentPart;
        if (mainPart is null)
        {
            return;
        }

        foreach (var headerPart in mainPart.HeaderParts)
        {
            var text = headerPart.Header?.InnerText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                elements.Add(PartitionerHelper.CreateElement(
                    ElementType.Header, text, fileName, ref index, docMetadata));
            }
        }

        foreach (var footerPart in mainPart.FooterParts)
        {
            var text = footerPart.Footer?.InnerText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                elements.Add(PartitionerHelper.CreateElement(
                    ElementType.Footer, text, fileName, ref index, docMetadata));
            }
        }
    }

    private static Dictionary<string, string> ExtractDocMetadata(WordprocessingDocument doc)
    {
        var metadata = new Dictionary<string, string> { [MetadataKeys.Format] = "DOCX" };
        var props = doc.PackageProperties;

        if (!string.IsNullOrWhiteSpace(props.Title))
        {
            metadata[MetadataKeys.Title] = props.Title;
        }

        if (!string.IsNullOrWhiteSpace(props.Creator))
        {
            metadata[MetadataKeys.Author] = props.Creator;
        }

        return metadata;
    }
}
