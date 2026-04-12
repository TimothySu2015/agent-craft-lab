using AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Engine.Services.Variables;

/// <summary>
/// 變數解析器 — 統一處理 {{sys:}} / {{var:}} / {{env:}} / {{node:}} / {{runtime:}} 五種引用。
/// 單一入口，取代 7 個 NodeExecutor 各自呼叫的分散邏輯。
/// </summary>
public interface IVariableResolver
{
    /// <summary>同步解析 — 無壓縮，直接替換。</summary>
    string Resolve(string? text, VariableContext context);

    /// <summary>非同步解析 — 當 {{node:}} 引用的輸出超過門檻時，透過 <see cref="IContextCompactor"/> 壓縮後注入。</summary>
    Task<string> ResolveAsync(
        string? text,
        VariableContext context,
        IContextCompactor compactor,
        string compressContext,
        CancellationToken cancellationToken = default);

    /// <summary>檢查文字中是否含有任何變數引用。</summary>
    bool HasReferences(string? text);

    /// <summary>提取文字中所有變數引用的 (scope, name) 組合（去重）。</summary>
    IReadOnlyList<VariableReference> ExtractReferences(string? text);
}

/// <summary>單一變數引用 — 例如 {{node:Researcher}} → new(NodeOutput, "Researcher")。</summary>
public readonly record struct VariableReference(VariableScope Scope, string Name);
