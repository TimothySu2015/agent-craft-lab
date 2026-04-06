namespace AgentCraftLab.Engine.Middleware;

/// <summary>
/// PII Middleware 的執行期選項。
/// </summary>
public sealed record PiiMaskingOptions
{
    /// <summary>信賴度門檻，低於此值的偵測結果將被忽略（預設 0.5）。</summary>
    public double ConfidenceThreshold { get; init; } = 0.5;

    /// <summary>不可逆模式下的替換文字（預設 "***"）。</summary>
    public string IrreversibleReplacement { get; init; } = "***";

    /// <summary>是否掃描 LLM 回應中的 PII（預設 true）。</summary>
    public bool ScanOutput { get; init; } = true;

    /// <summary>是否在回應中將 token 還原為原始值（預設 true，需搭配 vault）。</summary>
    public bool DetokenizeOutput { get; init; } = true;

    /// <summary>
    /// 從前端 config dictionary 建立選項。
    /// </summary>
    public static PiiMaskingOptions FromConfig(Dictionary<string, string>? config)
    {
        if (config is null || config.Count == 0)
        {
            return new PiiMaskingOptions();
        }

        var options = new PiiMaskingOptions();

        if (config.TryGetValue("confidenceThreshold", out var threshold) &&
            double.TryParse(threshold, out var t))
        {
            options = options with { ConfidenceThreshold = Math.Clamp(t, 0.0, 1.0) };
        }

        if (config.TryGetValue("replacement", out var replacement) &&
            !string.IsNullOrWhiteSpace(replacement))
        {
            options = options with { IrreversibleReplacement = replacement };
        }

        if (config.TryGetValue("scanOutput", out var scan))
        {
            options = options with { ScanOutput = !string.Equals(scan, "false", StringComparison.OrdinalIgnoreCase) };
        }

        if (config.TryGetValue("mode", out var mode))
        {
            // reversible 模式下 detokenize = true；irreversible 模式下 = false
            var isReversible = string.Equals(mode, "reversible", StringComparison.OrdinalIgnoreCase);
            options = options with { DetokenizeOutput = isReversible };
        }

        return options;
    }
}
