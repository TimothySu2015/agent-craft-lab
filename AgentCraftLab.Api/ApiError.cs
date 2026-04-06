namespace AgentCraftLab.Api;

/// <summary>
/// 統一 API 錯誤回應格式。
/// 前端根據 Code 查詢 i18n 翻譯，Params 提供插值變數，Message 作為 English fallback。
/// </summary>
public record ApiError(
    string Code,
    string? Message = null,
    Dictionary<string, string>? Params = null
);
