namespace AgentCraftLab.Engine.Models;

/// <summary>
/// 憑證欄位規格（描述某個 provider 需要哪些欄位）。
/// </summary>
public record CredentialFieldSpec(
    string FieldName,
    string Label,
    bool Required,
    bool IsSensitive = true,
    string? Placeholder = null);

/// <summary>
/// 某 provider 的完整憑證規格。
/// </summary>
public record CredentialSpec(
    string Provider,
    string DisplayName,
    IReadOnlyList<CredentialFieldSpec> Fields);

/// <summary>
/// 工具狀態快照。
/// </summary>
public record ToolStatus(
    string Id,
    string DisplayName,
    ToolCategory Category,
    string Icon,
    string Description,
    string? RequiredCredential,
    bool IsCredentialConfigured,
    bool IsEnabled,
    ToolAvailability Availability);

/// <summary>
/// 工具可用性。
/// </summary>
public enum ToolAvailability
{
    Ready,
    Disabled,
    MissingCredential
}

/// <summary>
/// 健康檢查結果。
/// </summary>
public record ToolHealthResult(
    string Id,
    string DisplayName,
    bool Success,
    string Message,
    long LatencyMs);
