namespace AgentCraftLab.Script;

/// <summary>
/// 沙箱白名單 API 介面 — 可注入到腳本引擎供腳本呼叫。
/// 不依賴具體引擎實作，各引擎自行將 GetMethods() 回傳的 delegate 註冊到自己的 context。
/// </summary>
public interface ISandboxApi
{
    /// <summary>API 名稱（在腳本中的全域物件名，如 "file"、"http"）。</summary>
    string Name { get; }

    /// <summary>API 方法清單（方法名 → delegate）。</summary>
    IReadOnlyDictionary<string, Delegate> GetMethods();
}
