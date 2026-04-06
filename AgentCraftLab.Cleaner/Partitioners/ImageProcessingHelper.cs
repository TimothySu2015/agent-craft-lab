using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.Partitioners;

/// <summary>
/// 圖片處理共用邏輯 — 被 ImagePartitioner / PptxPartitioner / PdfPartitioner 共用。
/// 處理預處理、去重快取、模式分流（OCR / AI 描述 / 混合）、fallback。
/// </summary>
internal static class ImageProcessingHelper
{
    /// <summary>上下文文字截取最大長度</summary>
    internal const int MaxContextTextLength = 500;

    /// <summary>
    /// 處理單張圖片。回傳 null 表示圖片被過濾（太小）。
    /// </summary>
    public static async Task<ImageProcessingResult?> ProcessImageAsync(
        ImageProcessingRequest request, CancellationToken ct)
    {
        // 預處理：過濾太小、縮放、格式轉換、hash
        ImagePreprocessResult? preprocessed;
        try
        {
            preprocessed = ImagePreprocessor.Process(request.ImageData, request.Options);
        }
        catch (Exception ex)
        {
            return new ImageProcessingResult
            {
                ElementType = ElementType.Image,
                Text = "[Image: unable to process]",
                Metadata =
                {
                    [MetadataKeys.ImageMode] = "failed",
                    [MetadataKeys.Error] = ex.GetType().Name,
                },
            };
        }

        if (preprocessed is null)
        {
            return null; // 太小，跳過
        }

        // 檢查去重快取
        if (request.Cache is not null &&
            request.Cache.TryGet(preprocessed.Hash, out var cached) && cached is not null)
        {
            return BuildResultFromDescription(cached, preprocessed);
        }

        // 根據模式分流
        var result = request.Mode switch
        {
            ImageProcessingMode.Ocr => await ProcessOcrAsync(
                preprocessed, request, ct),
            ImageProcessingMode.AiDescribe => await ProcessAiDescribeAsync(
                preprocessed, request, ct),
            ImageProcessingMode.Hybrid => await ProcessHybridAsync(
                preprocessed, request, ct),
            _ => null,
        };

        if (result is null)
        {
            return new ImageProcessingResult
            {
                ElementType = ElementType.Image,
                Text = "[Image: no description available]",
                Metadata =
                {
                    [MetadataKeys.ImageHash] = preprocessed.Hash,
                    [MetadataKeys.ImageWidth] = preprocessed.OriginalWidth.ToString(),
                    [MetadataKeys.ImageHeight] = preprocessed.OriginalHeight.ToString(),
                    [MetadataKeys.ImageMode] = request.Mode.ToString().ToLowerInvariant(),
                },
            };
        }

        // 存入快取
        if (request.Cache is not null && result.DescriptionResult is not null)
        {
            request.Cache.Set(preprocessed.Hash, result.DescriptionResult);
        }

        return result;
    }

    /// <summary>
    /// 批次處理圖片序列並組裝為 DocumentElement 清單。
    /// 由 PptxPartitioner / PdfPartitioner 共用，消除重複的遍歷邏輯。
    /// </summary>
    public static async Task<(List<DocumentElement> Elements, int NextIndex)> ProcessImageBatchAsync(
        IEnumerable<byte[]?> imageBytesSequence,
        string fileName,
        int pageNumber,
        Dictionary<string, string> pageMetadata,
        List<DocumentElement> textElements,
        PartitionOptions options,
        IOcrProvider? ocrProvider,
        IImageDescriber? imageDescriber,
        ImageDescriptionCache imageCache,
        int startIndex,
        CancellationToken ct)
    {
        var imageElements = new List<DocumentElement>();
        var index = startIndex;

        var context = BuildContext(textElements, fileName, pageNumber);
        var request = new ImageProcessingRequest
        {
            ImageData = [], // 每次迭代更新
            MimeType = "image/png",
            Mode = options.ImageMode,
            Context = context,
            Options = options,
            OcrProvider = ocrProvider,
            ImageDescriber = imageDescriber,
            Cache = imageCache,
        };

        foreach (var imageBytes in imageBytesSequence)
        {
            ct.ThrowIfCancellationRequested();

            if (imageBytes is null || imageBytes.Length == 0)
            {
                continue;
            }

            request = request with { ImageData = imageBytes };
            var result = await ProcessImageAsync(request, ct);

            if (result is null)
            {
                continue;
            }

            var elementMetadata = new Dictionary<string, string>(pageMetadata);
            foreach (var kvp in result.Metadata)
            {
                elementMetadata[kvp.Key] = kvp.Value;
            }

            imageElements.Add(new DocumentElement
            {
                Type = result.ElementType,
                Text = result.Text,
                FileName = fileName,
                PageNumber = pageNumber,
                Index = index++,
                Metadata = elementMetadata,
            });
        }

        return (imageElements, index);
    }

    /// <summary>從同頁文字元素建立圖片描述上下文</summary>
    internal static ImageDescriptionContext BuildContext(
        List<DocumentElement> textElements, string fileName, int pageNumber)
    {
        var pageTitle = textElements
            .FirstOrDefault(e => e.Type == ElementType.Title)?.Text;
        var pageText = string.Join("\n",
            textElements.Where(e => e.Type is ElementType.NarrativeText or ElementType.ListItem)
                .Select(e => e.Text));

        if (pageText.Length > MaxContextTextLength)
        {
            pageText = pageText[..MaxContextTextLength];
        }

        return new ImageDescriptionContext
        {
            PageTitle = pageTitle,
            PageText = string.IsNullOrWhiteSpace(pageText) ? null : pageText,
            FileName = fileName,
            PageNumber = pageNumber,
        };
    }

    private static async Task<ImageProcessingResult?> ProcessOcrAsync(
        ImagePreprocessResult preprocessed,
        ImageProcessingRequest request,
        CancellationToken ct)
    {
        if (request.OcrProvider is null)
        {
            return null;
        }

        var ocrResult = await request.OcrProvider.RecognizeAsync(
            preprocessed.ProcessedData, request.Options.OcrLanguages, ct);

        if (string.IsNullOrWhiteSpace(ocrResult.Text))
        {
            return new ImageProcessingResult
            {
                ElementType = ElementType.Image,
                Text = "[Image: no text detected]",
                Metadata =
                {
                    [MetadataKeys.ImageHash] = preprocessed.Hash,
                    [MetadataKeys.ImageWidth] = preprocessed.OriginalWidth.ToString(),
                    [MetadataKeys.ImageHeight] = preprocessed.OriginalHeight.ToString(),
                    [MetadataKeys.ImageMode] = "ocr",
                },
            };
        }

        return new ImageProcessingResult
        {
            ElementType = ElementType.UncategorizedText,
            Text = ocrResult.Text,
            Metadata =
            {
                [MetadataKeys.OcrConfidence] = ocrResult.Confidence.ToString("F2"),
                [MetadataKeys.ImageHash] = preprocessed.Hash,
                [MetadataKeys.ImageWidth] = preprocessed.OriginalWidth.ToString(),
                [MetadataKeys.ImageHeight] = preprocessed.OriginalHeight.ToString(),
                [MetadataKeys.ImageMode] = "ocr",
            },
        };
    }

    private static async Task<ImageProcessingResult?> ProcessAiDescribeAsync(
        ImagePreprocessResult preprocessed,
        ImageProcessingRequest request,
        CancellationToken ct)
    {
        if (request.ImageDescriber is not null)
        {
            try
            {
                var result = await request.ImageDescriber.DescribeAsync(
                    preprocessed.ProcessedData, preprocessed.MimeType, request.Context, ct);
                return BuildResultFromDescription(result, preprocessed);
            }
            catch (Exception ex)
            {
                // AI 失敗 → 記錄原因後 fallback 到 OCR
                var fallbackResult = await ProcessOcrAsync(preprocessed, request, ct);
                if (fallbackResult is not null)
                {
                    fallbackResult.Metadata[MetadataKeys.ImageMode] = "ai-describe-fallback-ocr";
                    fallbackResult.Metadata[MetadataKeys.Error] = ex.GetType().Name;
                    return fallbackResult;
                }

                return null;
            }
        }

        // 無 AI describer → 直接嘗試 OCR
        var ocrResult = await ProcessOcrAsync(preprocessed, request, ct);
        if (ocrResult is not null)
        {
            ocrResult.Metadata[MetadataKeys.ImageMode] = "ai-describe-fallback-ocr";
            return ocrResult;
        }

        return null;
    }

    private static async Task<ImageProcessingResult?> ProcessHybridAsync(
        ImagePreprocessResult preprocessed,
        ImageProcessingRequest request,
        CancellationToken ct)
    {
        // Step 1: OCR 先做
        if (request.OcrProvider is not null)
        {
            var ocrResult = await request.OcrProvider.RecognizeAsync(
                preprocessed.ProcessedData, request.Options.OcrLanguages, ct);

            // OCR 信心度足夠 → 直接用
            if (!string.IsNullOrWhiteSpace(ocrResult.Text) &&
                ocrResult.Confidence >= request.Options.HybridOcrThreshold)
            {
                return new ImageProcessingResult
                {
                    ElementType = ElementType.UncategorizedText,
                    Text = ocrResult.Text,
                    Metadata =
                    {
                        [MetadataKeys.OcrConfidence] = ocrResult.Confidence.ToString("F2"),
                        [MetadataKeys.ImageHash] = preprocessed.Hash,
                        [MetadataKeys.ImageWidth] = preprocessed.OriginalWidth.ToString(),
                        [MetadataKeys.ImageHeight] = preprocessed.OriginalHeight.ToString(),
                        [MetadataKeys.ImageMode] = "hybrid-ocr",
                    },
                };
            }
        }

        // Step 2: OCR 信心度不足或無 OCR → AI 描述
        return await ProcessAiDescribeAsync(preprocessed, request, ct);
    }

    private static ImageProcessingResult BuildResultFromDescription(
        ImageDescriptionResult result, ImagePreprocessResult preprocessed)
    {
        return new ImageProcessingResult
        {
            ElementType = ElementType.Image,
            Text = result.Description,
            DescriptionResult = result,
            Metadata =
            {
                [MetadataKeys.ImageHash] = preprocessed.Hash,
                [MetadataKeys.ImageWidth] = preprocessed.OriginalWidth.ToString(),
                [MetadataKeys.ImageHeight] = preprocessed.OriginalHeight.ToString(),
                [MetadataKeys.ImageMode] = "ai-describe",
                [MetadataKeys.DescriptionTokensIn] = result.InputTokens.ToString(),
                [MetadataKeys.DescriptionTokensOut] = result.OutputTokens.ToString(),
            },
        };
    }
}

/// <summary>圖片處理請求（封裝 ProcessImageAsync 的所有參數）</summary>
internal sealed record ImageProcessingRequest
{
    public required byte[] ImageData { get; init; }
    public required string MimeType { get; init; }
    public required ImageProcessingMode Mode { get; init; }
    public ImageDescriptionContext? Context { get; init; }
    public required PartitionOptions Options { get; init; }
    public IOcrProvider? OcrProvider { get; init; }
    public IImageDescriber? ImageDescriber { get; init; }
    public ImageDescriptionCache? Cache { get; init; }
}

/// <summary>圖片處理結果（內部傳遞用）</summary>
internal sealed class ImageProcessingResult
{
    public required ElementType ElementType { get; init; }
    public required string Text { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = [];
    public ImageDescriptionResult? DescriptionResult { get; init; }
}
