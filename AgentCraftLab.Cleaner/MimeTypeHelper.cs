namespace AgentCraftLab.Cleaner;

/// <summary>
/// MIME type 工具 — 統一副檔名到 MIME type 的映射，消除跨專案重複。
/// </summary>
public static class MimeTypeHelper
{
    public static string FromExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xls" => "application/vnd.ms-excel",
            ".html" or ".htm" => "text/html",
            ".md" => "text/markdown",
            ".csv" => "text/csv",
            ".tsv" => "text/tab-separated-values",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".txt" => "text/plain",
            ".cs" => "text/x-csharp",
            ".py" => "text/x-python",
            ".js" => "text/javascript",
            ".ts" => "text/x-typescript",
            ".java" => "text/x-java",
            ".go" => "text/x-go",
            ".rs" => "text/x-rust",
            ".yaml" or ".yml" => "text/x-yaml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".tiff" or ".tif" => "image/tiff",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            _ => "application/octet-stream",
        };
    }
}
