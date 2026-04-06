namespace AgentCraftLab.Cleaner.Abstractions;

/// <summary>
/// 輸出渲染器 — 將 Schema Mapper 產出的 JSON 轉為可讀格式。
/// </summary>
public interface IOutputRenderer
{
    /// <summary>支援的輸出格式名稱（如 "markdown", "html"）</summary>
    string Format { get; }

    /// <summary>渲染 Schema Mapper 結果為目標格式</summary>
    /// <param name="json">Schema Mapper 產出的 JSON</param>
    /// <param name="schema">原始 Schema 定義（用於取得欄位描述）</param>
    /// <param name="ct">取消 token</param>
    /// <returns>渲染後的內容字串</returns>
    Task<string> RenderAsync(string json, SchemaDefinition schema, CancellationToken ct = default);
}
