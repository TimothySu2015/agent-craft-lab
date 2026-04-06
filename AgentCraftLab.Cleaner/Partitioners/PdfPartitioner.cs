using System.Text;
using System.Text.RegularExpressions;
using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace AgentCraftLab.Cleaner.Partitioners;

/// <summary>
/// PDF 分割器 — 逐頁擷取文字，用啟發式規則分類元素類型。
/// 支援內嵌圖片提取（透過 IOcrProvider / IImageDescriber）。
/// PDF 沒有語意結構，只有座標和文字，所以分類精準度比 DOCX/PPTX 低。
/// 進階版面分析可透過替換為 VLM Partitioner 來改善。
/// </summary>
public sealed partial class PdfPartitioner : IPartitioner
{
    private const string SupportedMimeType = "application/pdf";
    private const int MaxTitleLength = 200;
    private const int MaxTitleLineCount = 2;
    private const int MaxPageNumberLength = 5;
    private const int MaxFooterLength = 100;

    private readonly IOcrProvider? _ocrProvider;
    private readonly IImageDescriber? _imageDescriber;

    public PdfPartitioner(IOcrProvider? ocrProvider = null, IImageDescriber? imageDescriber = null)
    {
        _ocrProvider = ocrProvider;
        _imageDescriber = imageDescriber;
    }

    public bool CanPartition(string mimeType) =>
        mimeType.Equals(SupportedMimeType, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<DocumentElement>> PartitionAsync(
        byte[] data,
        string fileName,
        PartitionOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new PartitionOptions();

        using var document = PdfDocument.Open(data);

        var elements = new List<DocumentElement>();
        var index = 0;

        var docMetadata = ExtractDocMetadata(document);
        var imageCache = new ImageDescriptionCache();

        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();

            var pageMetadata = new Dictionary<string, string>(docMetadata)
            {
                [MetadataKeys.PageNumber] = page.Number.ToString(),
            };

            // 處理文字
            var pageText = page.Text;
            var textElements = new List<DocumentElement>();

            if (!string.IsNullOrWhiteSpace(pageText))
            {
                var paragraphs = SplitIntoParagraphs(pageText);

                foreach (var para in paragraphs)
                {
                    if (string.IsNullOrWhiteSpace(para))
                    {
                        continue;
                    }

                    var elementType = ClassifyParagraph(para, page);
                    var element = PartitionerHelper.CreateElement(
                        elementType, para, fileName, ref index, pageMetadata, page.Number);
                    textElements.Add(element);
                }
            }

            elements.AddRange(textElements);

            // 處理圖片
            if (options.ImageMode != ImageProcessingMode.Skip)
            {
                var imageByteSequence = ExtractPageImageBytes(page);
                var (imageElements, nextIndex) = await ImageProcessingHelper.ProcessImageBatchAsync(
                    imageByteSequence, fileName, page.Number, pageMetadata, textElements,
                    options, _ocrProvider, _imageDescriber, imageCache, index, ct);
                index = nextIndex;
                elements.AddRange(imageElements);
            }
        }

        return elements;
    }

    /// <summary>從 PDF 頁面提取所有圖片的 bytes 序列</summary>
    private static IEnumerable<byte[]?> ExtractPageImageBytes(Page page)
    {
        IReadOnlyList<IPdfImage> images;
        try
        {
            images = page.GetImages().ToList();
        }
        catch
        {
            yield break; // PDF 圖片提取失敗 → 跳過
        }

        foreach (var pdfImage in images)
        {
            byte[]? imageBytes;
            try
            {
                if (!pdfImage.TryGetPng(out var pngBytes) || pngBytes is null || pngBytes.Length == 0)
                {
                    imageBytes = pdfImage.RawBytes.ToArray();
                }
                else
                {
                    imageBytes = pngBytes;
                }
            }
            catch
            {
                imageBytes = null; // 單張圖片提取失敗不中斷
            }

            yield return imageBytes;
        }
    }

    /// <summary>依空行分段</summary>
    private static List<string> SplitIntoParagraphs(string text)
    {
        var result = new List<string>();
        var sb = new StringBuilder();

        foreach (var line in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (sb.Length > 0)
                {
                    result.Add(sb.ToString().Trim());
                    sb.Clear();
                }
            }
            else
            {
                if (sb.Length > 0)
                {
                    sb.Append('\n');
                }
                sb.Append(line);
            }
        }

        if (sb.Length > 0)
        {
            result.Add(sb.ToString().Trim());
        }

        return result;
    }

    /// <summary>啟發式分類 PDF 段落</summary>
    private static ElementType ClassifyParagraph(string text, Page page)
    {
        var trimmed = text.Trim();

        // 短文字 + 全大寫或粗體特徵 → 可能是標題
        var lines = trimmed.Split('\n');
        if (lines.Length <= MaxTitleLineCount && trimmed.Length < MaxTitleLength)
        {
            // 全大寫英文 → Title
            if (trimmed.Length > 2 && trimmed == trimmed.ToUpperInvariant() && LatinLetters().IsMatch(trimmed))
            {
                return ElementType.Title;
            }

            // 以數字章節編號開頭：1. / 1.1 / Chapter 1 / 第一章
            if (ChapterHeading().IsMatch(trimmed))
            {
                return ElementType.Title;
            }
        }

        // 清單偵測
        if (BulletPattern().IsMatch(trimmed))
        {
            return ElementType.ListItem;
        }

        // 頁碼偵測（只有數字，且很短）
        if (trimmed.Length <= MaxPageNumberLength && PageNumberPattern().IsMatch(trimmed))
        {
            return ElementType.PageNumber;
        }

        // 頁首/頁尾偵測：位於頁面頂部或底部的短文字
        // 注意：PdfPig 的座標系統是左下角為原點
        if (trimmed.Length < MaxFooterLength && lines.Length == 1)
        {
            // 常見頁尾模式：「Page X of Y」、「第 X 頁」、「- X -」
            if (PageFooterPattern().IsMatch(trimmed))
            {
                return ElementType.Footer;
            }
        }

        // 表格偵測：多行且包含 tab 或多個連續空格分隔的數據
        if (lines.Length >= 2 && TabularPattern().IsMatch(trimmed))
        {
            return ElementType.Table;
        }

        return ElementType.NarrativeText;
    }

    private static Dictionary<string, string> ExtractDocMetadata(PdfDocument document)
    {
        var metadata = new Dictionary<string, string>
        {
            [MetadataKeys.PageCount] = document.NumberOfPages.ToString(),
            [MetadataKeys.Format] = "PDF",
        };

        var info = document.Information;
        if (!string.IsNullOrWhiteSpace(info.Title))
        {
            metadata[MetadataKeys.Title] = info.Title;
        }

        if (!string.IsNullOrWhiteSpace(info.Author))
        {
            metadata[MetadataKeys.Author] = info.Author;
        }

        return metadata;
    }

    [GeneratedRegex(@"[a-zA-Z]")]
    private static partial Regex LatinLetters();

    [GeneratedRegex(@"^(?:第[一二三四五六七八九十\d]+[章節篇]|Chapter\s+\d+|CHAPTER\s+\d+|\d+[\.\)]\s+\S)", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterHeading();

    [GeneratedRegex(@"^[\s]*(?:[•○●■□►▸▹\-\*\+]\s|\d+[\.\)]\s|\([a-zA-Z0-9]+\)\s)")]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex PageNumberPattern();

    [GeneratedRegex(@"(?:^Page\s+\d+|第\s*\d+\s*頁|^-\s*\d+\s*-)", RegexOptions.IgnoreCase)]
    private static partial Regex PageFooterPattern();

    // 至少 2 行中有 tab 或 3+ 連續空格
    [GeneratedRegex(@"(\t|   +).*\n.*(\t|   +)")]
    private static partial Regex TabularPattern();
}
