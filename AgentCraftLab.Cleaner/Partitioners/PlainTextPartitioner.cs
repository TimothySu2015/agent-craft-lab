using System.Text.RegularExpressions;
using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.Partitioners;

/// <summary>
/// 純文字 / Markdown 分割器 — 用 Regex 啟發式判斷元素類型。
/// 支援 txt, md, csv, json, 程式碼等。
/// </summary>
public sealed partial class PlainTextPartitioner : IPartitioner
{
    private static readonly HashSet<string> SupportedMimeTypes =
    [
        "text/plain", "text/markdown",
        "text/csv", "text/tab-separated-values",
        "application/json", "application/xml", "text/xml",
        "text/x-csharp", "text/x-python", "text/javascript",
        "text/x-java", "text/x-go", "text/x-rust",
        "text/x-typescript", "text/x-yaml", "application/x-yaml",
    ];

    // 程式碼 MIME types
    private static readonly HashSet<string> CodeMimeTypes =
    [
        "text/x-csharp", "text/x-python", "text/javascript",
        "text/x-java", "text/x-go", "text/x-rust",
        "text/x-typescript", "application/json", "application/xml",
        "text/xml", "text/x-yaml", "application/x-yaml",
    ];

    public bool CanPartition(string mimeType) =>
        SupportedMimeTypes.Contains(mimeType) ||
        mimeType.StartsWith("text/", StringComparison.Ordinal);

    public Task<IReadOnlyList<DocumentElement>> PartitionAsync(
        byte[] data,
        string fileName,
        PartitionOptions? options = null,
        CancellationToken ct = default)
    {
        var text = System.Text.Encoding.UTF8.GetString(data);
        var mimeType = GuessMimeType(fileName);

        var docMetadata = new Dictionary<string, string>
        {
            [MetadataKeys.Format] = "Text",
            [MetadataKeys.LineCount] = (text.AsSpan().Count('\n') + 1).ToString(),
        };

        // 程式碼檔案 → 整份當一個 CodeSnippet
        if (CodeMimeTypes.Contains(mimeType))
        {
            return Task.FromResult<IReadOnlyList<DocumentElement>>(
            [
                new DocumentElement
                {
                    Type = ElementType.CodeSnippet,
                    Text = text,
                    FileName = fileName,
                    Index = 0,
                    Metadata = docMetadata,
                }
            ]);
        }

        // CSV / TSV → 整份當 Table
        if (mimeType is "text/csv" or "text/tab-separated-values")
        {
            return Task.FromResult<IReadOnlyList<DocumentElement>>(
            [
                new DocumentElement
                {
                    Type = ElementType.Table,
                    Text = text,
                    FileName = fileName,
                    Index = 0,
                    Metadata = docMetadata,
                }
            ]);
        }

        // Markdown / 純文字 → 逐段分類
        var elements = PartitionByParagraphs(text, fileName, docMetadata, ct);
        return Task.FromResult<IReadOnlyList<DocumentElement>>(elements);
    }

    private static List<DocumentElement> PartitionByParagraphs(
        string text,
        string fileName,
        Dictionary<string, string> docMetadata,
        CancellationToken ct)
    {
        var elements = new List<DocumentElement>();
        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var index = 0;

        foreach (var para in paragraphs)
        {
            ct.ThrowIfCancellationRequested();

            var trimmed = para.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            var elementType = ClassifyParagraph(trimmed);
            elements.Add(new DocumentElement
            {
                Type = elementType,
                Text = trimmed,
                FileName = fileName,
                Index = index++,
                Metadata = new Dictionary<string, string>(docMetadata),
            });
        }

        return elements;
    }

    private static ElementType ClassifyParagraph(string text)
    {
        // Markdown 標題：# / ## / ### ...
        if (MarkdownHeading().IsMatch(text))
        {
            return ElementType.Title;
        }

        // Markdown 程式碼區塊：```
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            return ElementType.CodeSnippet;
        }

        // 清單：- / * / 1. / 1) 開頭
        if (BulletList().IsMatch(text))
        {
            return ElementType.ListItem;
        }

        // Markdown 表格：| xxx | xxx |
        if (MarkdownTable().IsMatch(text))
        {
            return ElementType.Table;
        }

        // 分隔線：--- / === / ***
        if (HorizontalRule().IsMatch(text))
        {
            return ElementType.PageBreak;
        }

        return ElementType.NarrativeText;
    }

    private static string GuessMimeType(string fileName) =>
        MimeTypeHelper.FromExtension(fileName);

    [GeneratedRegex(@"^#{1,6}\s+\S")]
    private static partial Regex MarkdownHeading();

    [GeneratedRegex(@"^[\s]*[\-\*\+]\s|^[\s]*\d+[\.\)]\s")]
    private static partial Regex BulletList();

    [GeneratedRegex(@"^\|.+\|")]
    private static partial Regex MarkdownTable();

    [GeneratedRegex(@"^[\-=\*]{3,}\s*$")]
    private static partial Regex HorizontalRule();
}
