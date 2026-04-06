using System.Collections.Concurrent;
using System.Text;

namespace AgentCraftLab.Autonomous.Flow.Models;

/// <summary>
/// Flow 級 Working Memory — 節點間共享的鍵值存儲。
/// Agent 節點透過 flow_memory_write tool 寫入，下游節點在 system prompt 中自動看到。
/// 生命週期：單次 Flow 執行（執行結束後清理）。
/// </summary>
public class FlowWorkingMemory
{
    private readonly ConcurrentDictionary<string, string> _store = new();

    /// <summary>寫入一個鍵值對（Agent 節點透過 meta-tool 呼叫）。</summary>
    public void Write(string key, string value) => _store[key] = value;

    /// <summary>讀取指定鍵的值。</summary>
    public string? Read(string key) => _store.GetValueOrDefault(key);

    /// <summary>取得所有鍵值對的唯讀快照。</summary>
    public IReadOnlyDictionary<string, string> Snapshot() => _store;

    /// <summary>是否為空。</summary>
    public bool IsEmpty => _store.IsEmpty;

    /// <summary>
    /// 產生注入 system prompt 的文字區塊。空 memory 回傳空字串。
    /// </summary>
    public string ToPromptSection()
    {
        if (_store.IsEmpty)
        {
            return "";
        }

        var sb = new StringBuilder();
        sb.AppendLine("\n[Shared Working Memory — data stored by previous nodes]");
        foreach (var (key, value) in _store)
        {
            sb.AppendLine($"- {key}: {value}");
        }

        return sb.ToString();
    }
}
