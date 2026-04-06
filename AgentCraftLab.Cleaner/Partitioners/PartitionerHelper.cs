using System.Text;
using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.Partitioners;

/// <summary>
/// Partitioner 共用工具 — 消除跨 Partitioner 的重複程式碼。
/// </summary>
internal static class PartitionerHelper
{
    /// <summary>將二維字串資料轉為 Markdown 表格（第一列為標題列）</summary>
    public static string ToMarkdownTable(IEnumerable<IEnumerable<string>> rows)
    {
        var sb = new StringBuilder();
        var rowIndex = 0;

        foreach (var row in rows)
        {
            var cells = row.ToList();
            sb.Append('|');
            foreach (var cell in cells)
            {
                sb.Append(' ').Append(cell.Trim()).Append(" |");
            }
            sb.AppendLine();

            if (rowIndex == 0)
            {
                sb.Append('|');
                for (var c = 0; c < cells.Count; c++)
                {
                    sb.Append(" --- |");
                }
                sb.AppendLine();
            }

            rowIndex++;
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>建立 DocumentElement（減少每個 Partitioner 的重複程式碼）</summary>
    public static DocumentElement CreateElement(
        ElementType type,
        string text,
        string fileName,
        ref int index,
        Dictionary<string, string> metadata,
        int? pageNumber = null) => new()
    {
        Type = type,
        Text = text,
        FileName = fileName,
        PageNumber = pageNumber,
        Index = index++,
        Metadata = new Dictionary<string, string>(metadata),
    };
}

/// <summary>Metadata key 常數 — 統一所有 Partitioner 的 metadata 命名</summary>
internal static class MetadataKeys
{
    public const string Format = "format";
    public const string Title = "title";
    public const string Author = "author";
    public const string PageCount = "page_count";
    public const string PageNumber = "page_number";
    public const string SlideNumber = "slide_number";
    public const string SlideCount = "slide_count";
    public const string SheetName = "sheet_name";
    public const string SheetCount = "sheet_count";
    public const string LineCount = "line_count";
    public const string FileSize = "file_size";
    public const string OcrConfidence = "ocr_confidence";
    public const string Description = "description";
    public const string ImageHash = "image_hash";
    public const string ImageWidth = "image_width";
    public const string ImageHeight = "image_height";
    public const string ImageMode = "image_processing_mode";
    public const string DescriptionTokensIn = "description_tokens_in";
    public const string DescriptionTokensOut = "description_tokens_out";
    public const string Error = "error";
}
