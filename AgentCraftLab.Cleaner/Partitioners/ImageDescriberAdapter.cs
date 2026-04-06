using AgentCraftLab.Cleaner.Abstractions;

namespace AgentCraftLab.Cleaner.Partitioners;

/// <summary>
/// Delegate-based adapter — 讓 Cleaner 層不直接依賴多模態 LLM SDK。
/// 與 <see cref="OcrEngineAdapter"/> 相同的解耦 pattern。
/// </summary>
public sealed class ImageDescriberAdapter : IImageDescriber
{
    private readonly Func<byte[], string, ImageDescriptionContext?, CancellationToken,
        Task<ImageDescriptionResult>> _describeFunc;

    public ImageDescriberAdapter(
        Func<byte[], string, ImageDescriptionContext?, CancellationToken,
            Task<ImageDescriptionResult>> describeFunc)
    {
        _describeFunc = describeFunc ?? throw new ArgumentNullException(nameof(describeFunc));
    }

    public Task<ImageDescriptionResult> DescribeAsync(
        byte[] imageData,
        string mimeType,
        ImageDescriptionContext? context = null,
        CancellationToken ct = default) =>
        _describeFunc(imageData, mimeType, context, ct);
}
