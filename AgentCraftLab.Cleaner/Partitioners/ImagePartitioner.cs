using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.Partitioners;

/// <summary>
/// 圖片分割器 — 支援 4 種圖片處理模式：Skip / OCR / AI 描述 / 混合。
/// 透過 IOcrProvider 和 IImageDescriber 介面解耦，不依賴特定 OCR 或 LLM SDK。
/// </summary>
public sealed class ImagePartitioner : IPartitioner
{
    private static readonly HashSet<string> SupportedMimeTypes =
    [
        "image/png",
        "image/jpeg",
        "image/jpg",
        "image/tiff",
        "image/bmp",
        "image/webp",
    ];

    private readonly IOcrProvider? _ocrProvider;
    private readonly IImageDescriber? _imageDescriber;

    public ImagePartitioner(IOcrProvider? ocrProvider = null, IImageDescriber? imageDescriber = null)
    {
        _ocrProvider = ocrProvider;
        _imageDescriber = imageDescriber;
    }

    public bool CanPartition(string mimeType) =>
        SupportedMimeTypes.Contains(mimeType.ToLowerInvariant());

    public async Task<IReadOnlyList<DocumentElement>> PartitionAsync(
        byte[] data,
        string fileName,
        PartitionOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new PartitionOptions();

        var docMetadata = new Dictionary<string, string>
        {
            [MetadataKeys.Format] = "Image",
            [MetadataKeys.FileSize] = data.Length.ToString(),
        };

        // 向下相容：ImageMode=Skip 但 EnableOcr=true → 走舊的直接 OCR 路徑（不經 ImagePreprocessor）
        if (options.ImageMode == ImageProcessingMode.Skip && options.EnableOcr && _ocrProvider is not null)
        {
            return await LegacyOcrAsync(data, fileName, options, docMetadata, ct);
        }

        // 新模式
        if (options.ImageMode == ImageProcessingMode.Skip)
        {
            return [CreatePlaceholder(fileName, docMetadata)];
        }

        var request = new ImageProcessingRequest
        {
            ImageData = data,
            MimeType = MimeTypeHelper.FromExtension(fileName),
            Mode = options.ImageMode,
            Context = new ImageDescriptionContext { FileName = fileName },
            Options = options,
            OcrProvider = _ocrProvider,
            ImageDescriber = _imageDescriber,
        };

        var result = await ImageProcessingHelper.ProcessImageAsync(request, ct);

        if (result is null)
        {
            return [CreatePlaceholder(fileName, docMetadata, "(too small)")];
        }

        foreach (var kvp in result.Metadata)
        {
            docMetadata[kvp.Key] = kvp.Value;
        }

        return
        [
            new DocumentElement
            {
                Type = result.ElementType,
                Text = result.Text,
                FileName = fileName,
                Index = 0,
                Metadata = docMetadata,
            }
        ];
    }

    /// <summary>舊的直接 OCR 路徑（不經 ImagePreprocessor，向下相容）</summary>
    private async Task<IReadOnlyList<DocumentElement>> LegacyOcrAsync(
        byte[] data, string fileName, PartitionOptions options,
        Dictionary<string, string> docMetadata, CancellationToken ct)
    {
        var ocrResult = await _ocrProvider!.RecognizeAsync(data, options.OcrLanguages, ct);

        if (string.IsNullOrWhiteSpace(ocrResult.Text))
        {
            return
            [
                new DocumentElement
                {
                    Type = ElementType.Image,
                    Text = $"[Image: {fileName} (no text detected)]",
                    FileName = fileName,
                    Index = 0,
                    Metadata = docMetadata,
                }
            ];
        }

        docMetadata[MetadataKeys.OcrConfidence] = ocrResult.Confidence.ToString("F2");

        return
        [
            new DocumentElement
            {
                Type = ElementType.UncategorizedText,
                Text = ocrResult.Text,
                FileName = fileName,
                Index = 0,
                Metadata = docMetadata,
            }
        ];
    }

    private static DocumentElement CreatePlaceholder(
        string fileName, Dictionary<string, string> metadata, string? suffix = null)
    {
        var text = suffix is not null
            ? $"[Image: {fileName} {suffix}]"
            : $"[Image: {fileName}]";

        return new DocumentElement
        {
            Type = ElementType.Image,
            Text = text,
            FileName = fileName,
            Index = 0,
            Metadata = metadata,
        };
    }

}
