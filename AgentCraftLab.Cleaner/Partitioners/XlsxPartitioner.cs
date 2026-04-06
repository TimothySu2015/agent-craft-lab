using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;
using ClosedXML.Excel;

namespace AgentCraftLab.Cleaner.Partitioners;

/// <summary>
/// Excel (.xlsx) 分割器 — 每個工作表產生一個 Table 元素（Markdown 格式）。
/// </summary>
public sealed class XlsxPartitioner : IPartitioner
{
    private static readonly HashSet<string> SupportedMimeTypes =
    [
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-excel",
    ];

    /// <summary>單一工作表最大讀取列數（防止超大檔案記憶體爆炸）</summary>
    private const int MaxRows = 10_000;

    public bool CanPartition(string mimeType) =>
        SupportedMimeTypes.Contains(mimeType.ToLowerInvariant());

    public Task<IReadOnlyList<DocumentElement>> PartitionAsync(
        byte[] data,
        string fileName,
        PartitionOptions? options = null,
        CancellationToken ct = default)
    {
        using var stream = new MemoryStream(data);
        using var workbook = new XLWorkbook(stream);

        var elements = new List<DocumentElement>();
        var index = 0;

        var docMetadata = new Dictionary<string, string>
        {
            [MetadataKeys.Format] = "XLSX",
            [MetadataKeys.SheetCount] = workbook.Worksheets.Count.ToString(),
        };

        foreach (var worksheet in workbook.Worksheets)
        {
            ct.ThrowIfCancellationRequested();

            var rangeUsed = worksheet.RangeUsed();
            if (rangeUsed is null)
            {
                continue;
            }

            var sheetMetadata = new Dictionary<string, string>(docMetadata)
            {
                [MetadataKeys.SheetName] = worksheet.Name,
            };

            var tableText = ExtractSheetAsMarkdownTable(worksheet, rangeUsed);
            if (!string.IsNullOrWhiteSpace(tableText))
            {
                elements.Add(PartitionerHelper.CreateElement(
                    ElementType.Table, tableText, fileName, ref index, sheetMetadata));
            }
        }

        return Task.FromResult<IReadOnlyList<DocumentElement>>(elements);
    }

    private static string ExtractSheetAsMarkdownTable(IXLWorksheet worksheet, IXLRange rangeUsed)
    {
        var firstRow = rangeUsed.FirstRow().RowNumber();
        var lastRow = Math.Min(rangeUsed.LastRow().RowNumber(), firstRow + MaxRows - 1);
        var firstCol = rangeUsed.FirstColumn().ColumnNumber();
        var lastCol = rangeUsed.LastColumn().ColumnNumber();

        if (lastRow < firstRow || lastCol < firstCol)
        {
            return "";
        }

        var rows = Enumerable.Range(firstRow, lastRow - firstRow + 1)
            .Select(r => Enumerable.Range(firstCol, lastCol - firstCol + 1)
                .Select(c => GetCellDisplayValue(worksheet.Cell(r, c))));

        return PartitionerHelper.ToMarkdownTable(rows);
    }

    private static string GetCellDisplayValue(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return "";
        }

        try
        {
            var formatted = cell.GetFormattedString();
            return formatted.Replace("|", "\\|").Replace("\n", " ").Trim();
        }
        catch (FormatException)
        {
            return cell.Value.ToString().Replace("|", "\\|").Replace("\n", " ").Trim();
        }
    }
}
