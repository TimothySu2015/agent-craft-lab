using System.ComponentModel;
using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 資料清洗工具註冊 — 將 CraftCleaner 掛到 ToolRegistryService，
/// 讓 Agent 可透過 function calling 清洗文件。
/// </summary>
public static class CleanerToolRegistration
{
    /// <summary>
    /// 註冊 document_clean 工具到平台工具目錄。
    /// </summary>
    public static void RegisterCleanerTools(this ToolRegistryService registry, IDocumentCleaner cleaner, string workingDirectory)
    {
        registry.Register("document_clean", "Document Clean", "清洗文件：去頁首頁尾、正規化空白、合併截斷段落、表格結構化（支援 PDF/DOCX/PPTX/XLSX/HTML/TXT/圖片）",
            () => AIFunctionFactory.Create(
                ([Description("檔案路徑（相對於工作目錄）")] string filePath,
                 [Description("是否移除頁首頁尾（預設 true）")] bool removeHeaderFooter = true) =>
                    CleanDocumentAsync(filePath, removeHeaderFooter, cleaner, workingDirectory),
                name: "DocumentClean",
                description: "清洗文件：去雜訊、正規化、結構化輸出"),
            ToolCategory.Data, "\U0001F9F9");
    }

    internal static async Task<string> CleanDocumentAsync(
        string filePath, bool removeHeaderFooter, IDocumentCleaner cleaner, string workingDirectory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return "Error: filePath is required.";
            }

            var resolvedPath = Path.GetFullPath(filePath, workingDirectory);
            if (!resolvedPath.StartsWith(workingDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return "Error: path traversal not allowed.";
            }

            if (!File.Exists(resolvedPath))
            {
                return $"Error: file not found: {filePath}";
            }

            var data = await File.ReadAllBytesAsync(resolvedPath);
            var mimeType = GuessMimeType(resolvedPath);

            var options = new CleaningOptions
            {
                ExcludeElementTypes = removeHeaderFooter
                    ? null  // 用預設 RemoveHeaderFooterFilter
                    : new HashSet<Cleaner.Elements.ElementType>(),  // 不排除任何類型
            };

            var result = await cleaner.CleanAsync(data, Path.GetFileName(resolvedPath), mimeType, options);
            var elementSummary = result.Elements
                .GroupBy(e => e.Type)
                .Select(g => $"{g.Key}: {g.Count()}")
                .ToList();

            return $"Cleaned: {result.Elements.Count} elements ({string.Join(", ", elementSummary)})\n\n{result.GetFullText()}";
        }
        catch (NotSupportedException)
        {
            return $"Error: unsupported file format: {Path.GetExtension(filePath)}";
        }
        catch (Exception ex)
        {
            return $"Document clean failed: {ex.Message}";
        }
    }

    private static string GuessMimeType(string path) =>
        Cleaner.MimeTypeHelper.FromExtension(path);
}
