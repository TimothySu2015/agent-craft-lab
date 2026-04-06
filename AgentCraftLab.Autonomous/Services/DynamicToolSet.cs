using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 動態工具集 — 管理 always-available 與按需載入的工具。
/// Thread-safe，供 ReactExecutor 主迴圈與 AgentPool 共享。
/// </summary>
public sealed class DynamicToolSet
{
    private readonly List<AITool> _alwaysAvailable;
    private readonly HashSet<string> _alwaysAvailableNames;
    private readonly ConcurrentDictionary<string, AITool> _loaded = new(StringComparer.OrdinalIgnoreCase);

    public DynamicToolSet(IEnumerable<AITool> alwaysAvailable)
    {
        _alwaysAvailable = [.. alwaysAvailable];
        _alwaysAvailableNames = new HashSet<string>(
            _alwaysAvailable.OfType<AIFunction>().Select(f => f.Name),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>永遠可用的工具（meta-tools + 安全白名單）。</summary>
    public IReadOnlyList<AITool> AlwaysAvailable => _alwaysAvailable;

    /// <summary>已動態載入的工具數量。</summary>
    public int LoadedCount => _loaded.Count;

    /// <summary>
    /// 取得目前所有可用工具（always-available + 已載入），供 ChatOptions.Tools 使用。
    /// 每次呼叫都回傳新 snapshot，確保 FunctionInvokingChatClient 看到最新工具清單。
    /// </summary>
    public IList<AITool> GetActiveTools()
    {
        var result = new List<AITool>(_alwaysAvailable.Count + _loaded.Count);
        result.AddRange(_alwaysAvailable);
        result.AddRange(_loaded.Values);
        return result;
    }

    /// <summary>
    /// 從 ToolSearchIndex 載入工具到當前執行階段。
    /// </summary>
    /// <returns>成功載入的工具名稱清單（已載入的不重複載入）。</returns>
    public List<string> LoadTools(IEnumerable<string> names, ToolSearchIndex index)
    {
        var loaded = new List<string>();
        foreach (var name in names)
        {
            // 已在 always-available 中 → 跳過
            if (IsAlwaysAvailable(name))
            {
                continue;
            }

            // 已載入 → 跳過
            if (_loaded.ContainsKey(name))
            {
                continue;
            }

            var tool = index.FindByName(name);
            if (tool is not null && _loaded.TryAdd(name, tool))
            {
                loaded.Add(name);
            }
        }

        return loaded;
    }

    /// <summary>直接載入一個已建立的工具（供 create_tool 使用，不經 ToolSearchIndex）。</summary>
    public bool LoadCreatedTool(string name, AITool tool)
    {
        return _loaded.TryAdd(name, tool);
    }

    /// <summary>取得已動態載入的工具名稱清單（供 checkpoint 使用）。</summary>
    public IEnumerable<string> GetLoadedNames()
    {
        return _loaded.Keys;
    }

    /// <summary>卸載指定工具。</summary>
    public bool Unload(string name)
    {
        return _loaded.TryRemove(name, out _);
    }

    /// <summary>檢查指定工具是否已可用（always-available 或已載入）。</summary>
    public bool IsAvailable(string name)
    {
        return IsAlwaysAvailable(name) || _loaded.ContainsKey(name);
    }

    private bool IsAlwaysAvailable(string name)
    {
        return _alwaysAvailableNames.Contains(name);
    }
}
