using System.Runtime.CompilerServices;
using System.Text;
using AgentCraftLab.Engine.Pii;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Middleware;

/// <summary>
/// PII 保護中介層。在訊息送出前偵測並遮罩 PII，支援可逆 token 化與雙向掃描。
/// <para>
/// 可逆模式（有 vault）：PII 以型別化 token（如 [EMAIL_1]）替換，LLM 回應後自動還原。
/// 不可逆模式（無 vault）：PII 以固定文字（如 ***）替換，向下相容舊版行為。
/// </para>
/// </summary>
public class PiiMaskingChatClient : DelegatingChatClient
{
    /// <summary>串流 detokenize buffer 上限（超過時強制 flush，避免非 token 的 [ 造成無限累積）。</summary>
    private const int StreamBufferFlushThreshold = 500;

    private readonly IPiiDetector _detector;
    private readonly IPiiTokenVault? _vault;
    private readonly PiiMaskingOptions _options;
    private readonly ILogger<PiiMaskingChatClient>? _logger;
    private readonly string _sessionKey;

    /// <summary>
    /// 新版建構子：使用 IPiiDetector + IPiiTokenVault 提供完整功能。
    /// </summary>
    public PiiMaskingChatClient(
        IChatClient innerClient,
        IPiiDetector detector,
        IPiiTokenVault? vault = null,
        PiiMaskingOptions? options = null,
        ILogger<PiiMaskingChatClient>? logger = null)
        : base(innerClient)
    {
        _detector = detector;
        _vault = vault;
        _options = options ?? new PiiMaskingOptions();
        _logger = logger;
        _sessionKey = Guid.NewGuid().ToString("N");

        _logger?.LogInformation(
            "[PII] Initialized: mode={Mode}, threshold={Threshold}, scanOutput={ScanOutput}",
            _vault is not null ? "reversible" : "irreversible",
            _options.ConfidenceThreshold,
            _options.ScanOutput);
    }

    /// <summary>
    /// 舊版建構子（向下相容）：從 config dictionary 建立內部 RegexPiiDetector，不可逆模式。
    /// </summary>
    public PiiMaskingChatClient(
        IChatClient innerClient,
        Dictionary<string, string>? config = null,
        ILogger<PiiMaskingChatClient>? logger = null)
        : this(
            innerClient,
            RegexPiiDetector.FromConfig(config),
            vault: null,
            PiiMaskingOptions.FromConfig(config),
            logger)
    {
    }

    /// <inheritdoc/>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var masked = AnonymizeMessages(messages);
        var response = await base.GetResponseAsync(masked, options, cancellationToken);

        // Output 處理：還原 token
        if (_vault is not null && _options.DetokenizeOutput && response.Text is { } responseText)
        {
            var detokenized = _vault.Detokenize(_sessionKey, responseText);
            if (detokenized != responseText)
            {
                _logger?.LogInformation("[PII] Direction=Output, Action=Detokenized");
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, detokenized));
            }
        }

        return response;
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var masked = AnonymizeMessages(messages);

        if (_vault is null || !_options.DetokenizeOutput)
        {
            // 無 vault 或不需還原：直接透傳
            await foreach (var update in base.GetStreamingResponseAsync(masked, options, cancellationToken))
            {
                yield return update;
            }
            yield break;
        }

        // 有 vault：累積 buffer，遇到完整 token 時還原
        var buffer = new StringBuilder();
        await foreach (var update in base.GetStreamingResponseAsync(masked, options, cancellationToken))
        {
            if (update.Text is not { } chunk)
            {
                yield return update;
                continue;
            }

            buffer.Append(chunk);

            // 若 buffer 中沒有未閉合的 [，可以安全 flush
            var text = buffer.ToString();
            var lastOpen = text.LastIndexOf('[');
            var lastClose = text.LastIndexOf(']');

            if (lastOpen < 0 || lastClose > lastOpen || buffer.Length > StreamBufferFlushThreshold)
            {
                // 無未閉合 token 或 buffer 過大（不太可能是 token），flush
                var detokenized = _vault.Detokenize(_sessionKey, text);
                buffer.Clear();
                yield return new ChatResponseUpdate(update.Role, detokenized);
            }
            // 有未閉合 [，繼續累積（等 ] 到來）
        }

        // flush 剩餘 buffer
        if (buffer.Length > 0)
        {
            var remaining = _vault.Detokenize(_sessionKey, buffer.ToString());
            yield return new ChatResponseUpdate(ChatRole.Assistant, remaining);
        }
    }

    /// <summary>遮罩 User 訊息中的 PII。</summary>
    private List<ChatMessage> AnonymizeMessages(IEnumerable<ChatMessage> messages)
    {
        var result = new List<ChatMessage>();
        foreach (var msg in messages)
        {
            if (msg.Role == ChatRole.User && msg.Text is { } text)
            {
                var anonymized = AnonymizeText(text);
                if (anonymized != text)
                {
                    result.Add(new ChatMessage(msg.Role, anonymized));
                    continue;
                }
            }
            result.Add(msg);
        }
        return result;
    }

    /// <summary>偵測並遮罩文字中的 PII。</summary>
    private string AnonymizeText(string text)
    {
        var entities = _detector.Detect(text, _options.ConfidenceThreshold);
        if (entities.Count == 0)
        {
            return text;
        }

        // 審計日誌（不記錄 PII 原始值）
        var typeCounts = entities
            .GroupBy(e => $"{e.Locale}.{e.Type}")
            .Select(g => $"{g.Key}:{g.Count()}")
            .ToList();

        _logger?.LogInformation(
            "[PII] Direction=Input, Entities=[{EntityTypes}], Count={Count}",
            string.Join(", ", typeCounts),
            entities.Count);

        // 右到左替換（entities 已依 Start 降序排列）
        var sb = new StringBuilder(text);
        foreach (var entity in entities)
        {
            var replacement = _vault is not null
                ? _vault.Tokenize(_sessionKey, entity.Text, entity.Type)
                : _options.IrreversibleReplacement;

            sb.Remove(entity.Start, entity.Length);
            sb.Insert(entity.Start, replacement);
        }

        return sb.ToString();
    }
}
