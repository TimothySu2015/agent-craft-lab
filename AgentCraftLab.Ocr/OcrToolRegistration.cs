using System.ComponentModel;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Ocr;

/// <summary>
/// OCR 工具註冊 — 將 OCR 辨識工具掛到 ToolRegistryService。
/// </summary>
public static class OcrToolRegistration
{
    /// <summary>
    /// 註冊 OCR 工具到平台工具目錄。由 UseOcrTools() 自動呼叫。
    /// </summary>
    public static void RegisterOcrTools(this ToolRegistryService registry, IOcrEngine ocrEngine, string workingDirectory)
    {
        registry.Register("ocr_recognize", "OCR - Recognize Image", "使用 OCR 從圖片辨識文字（支援 PNG/JPG/TIFF/BMP，預設辨識繁中/簡中/英文/日文/韓文）",
            () => AIFunctionFactory.Create(
                ([Description("圖片檔案路徑（相對於工作目錄），支援 PNG, JPG, TIFF, BMP")] string imagePath,
                 [Description("語言代碼（逗號分隔，如 eng,chi_tra,jpn），留空使用預設（繁中+簡中+英文+日文+韓文）")] string languages = "") =>
                    RecognizeImageAsync(imagePath, languages, ocrEngine, workingDirectory),
                name: "OcrRecognize",
                description: "使用 OCR 從圖片辨識文字（支援多語言）"),
            ToolCategory.Data, "\U0001F5BC");
    }

    internal static async Task<string> RecognizeImageAsync(
        string imagePath, string languages, IOcrEngine ocrEngine, string workingDirectory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return "Error: imagePath is required.";
            }

            // 解析為絕對路徑（限制在 workingDirectory 內，防止路徑穿越）
            var resolvedPath = Path.GetFullPath(imagePath, workingDirectory);
            if (!resolvedPath.StartsWith(workingDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return "Error: path traversal not allowed.";
            }

            if (!File.Exists(resolvedPath))
            {
                return $"Error: file not found: {imagePath}";
            }

            var imageData = await File.ReadAllBytesAsync(resolvedPath);

            var langs = string.IsNullOrWhiteSpace(languages)
                ? null
                : languages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList() as IReadOnlyList<string>;

            var result = await ocrEngine.RecognizeAsync(imageData, langs);

            if (!result.HasContent)
            {
                return "No text recognized in the image.";
            }

            return $"Recognized text (confidence: {result.Confidence:P0}):\n\n{result.Text}";
        }
        catch (Exception ex)
        {
            return $"OCR failed: {ex.Message}";
        }
    }
}
