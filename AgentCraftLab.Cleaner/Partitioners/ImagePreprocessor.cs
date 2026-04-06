using System.Security.Cryptography;
using AgentCraftLab.Cleaner.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace AgentCraftLab.Cleaner.Partitioners;

/// <summary>
/// 圖片預處理器 — 過濾、縮放、格式轉換、hash 計算。
/// 確保送給 AI 的圖片在合理尺寸範圍內，並支援去重。
/// </summary>
internal static class ImagePreprocessor
{
    /// <summary>
    /// 預處理圖片。回傳 null 表示圖片應被跳過（太小）。
    /// </summary>
    public static ImagePreprocessResult? Process(byte[] imageData, PartitionOptions options)
    {
        using var image = Image.Load(imageData);

        // 太小的圖片（icon、裝飾圖）→ 跳過
        if (image.Width < options.MinImageWidth || image.Height < options.MinImageHeight)
        {
            return null;
        }

        var originalWidth = image.Width;
        var originalHeight = image.Height;
        var wasResized = false;

        // 長邊超過上限 → 等比縮放
        var maxDim = options.MaxImageDimension;
        if (image.Width > maxDim || image.Height > maxDim)
        {
            var scale = (float)maxDim / Math.Max(image.Width, image.Height);
            var newWidth = (int)(image.Width * scale);
            var newHeight = (int)(image.Height * scale);
            image.Mutate(x => x.Resize(newWidth, newHeight));
            wasResized = true;
        }

        // 統一輸出 PNG（所有多模態 LLM 都支援）
        using var output = new MemoryStream();
        image.Save(output, new PngEncoder());
        var processedData = output.ToArray();

        // SHA-256 hash（去重用）
        var hash = Convert.ToHexString(SHA256.HashData(processedData)).ToLowerInvariant();

        return new ImagePreprocessResult
        {
            ProcessedData = processedData,
            MimeType = "image/png",
            Hash = hash,
            OriginalWidth = originalWidth,
            OriginalHeight = originalHeight,
            Width = image.Width,
            Height = image.Height,
            WasResized = wasResized,
        };
    }
}

/// <summary>圖片預處理結果</summary>
internal sealed class ImagePreprocessResult
{
    /// <summary>處理後的圖片 bytes</summary>
    public required byte[] ProcessedData { get; init; }

    /// <summary>處理後的 MIME type（統一為 image/png）</summary>
    public required string MimeType { get; init; }

    /// <summary>SHA-256 hash（去重用）</summary>
    public required string Hash { get; init; }

    /// <summary>原始寬度</summary>
    public int OriginalWidth { get; init; }

    /// <summary>原始高度</summary>
    public int OriginalHeight { get; init; }

    /// <summary>處理後寬度</summary>
    public required int Width { get; init; }

    /// <summary>處理後高度</summary>
    public required int Height { get; init; }

    /// <summary>是否執行了縮放</summary>
    public bool WasResized { get; init; }
}
