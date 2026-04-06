using Microsoft.Extensions.AI;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 工具搜尋索引 — 索引所有可搜尋的工具，支援關鍵字查詢。
/// 供 search_tools meta-tool 使用，讓 Agent 按需發現工具。
/// </summary>
public sealed class ToolSearchIndex
{
    private readonly List<ToolIndexEntry> _entries;
    private readonly Dictionary<string, AITool> _nameIndex;

    public ToolSearchIndex(IEnumerable<AITool> searchableTools)
    {
        _entries = [];
        _nameIndex = new Dictionary<string, AITool>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in searchableTools)
        {
            if (tool is AIFunction func)
            {
                var name = func.Name;
                var desc = func.Description ?? "";
                _entries.Add(new ToolIndexEntry(
                    name, desc, tool, Tokenize(name), Tokenize(desc)));
                _nameIndex.TryAdd(name, tool);
            }
        }
    }

    /// <summary>索引中的工具總數。</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// 搜尋工具 — 對 query 做分詞後與工具名稱+描述比對，回傳相關度最高的結果。
    /// </summary>
    public List<ToolSearchResult> Search(string query, int maxResults = 5)
    {
        if (string.IsNullOrWhiteSpace(query) || _entries.Count == 0)
        {
            return [];
        }

        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
        {
            return [];
        }

        var scored = new List<(ToolIndexEntry Entry, double Score)>();

        foreach (var entry in _entries)
        {
            var score = ComputeRelevance(queryTokens, entry);
            if (score > 0)
            {
                scored.Add((entry, score));
            }
        }

        return scored
            .OrderByDescending(s => s.Score)
            .Take(maxResults)
            .Select(s => new ToolSearchResult(s.Entry.Name, s.Entry.Description, s.Score))
            .ToList();
    }

    /// <summary>
    /// 依名稱查詢工具（精確匹配，大小寫不敏感）。
    /// </summary>
    public AITool? FindByName(string name)
    {
        return _nameIndex.GetValueOrDefault(name);
    }

    /// <summary>
    /// 依名稱批次查詢工具。
    /// </summary>
    public List<AITool> FindByNames(IEnumerable<string> names)
    {
        var result = new List<AITool>();
        foreach (var name in names)
        {
            if (_nameIndex.TryGetValue(name, out var tool))
            {
                result.Add(tool);
            }
        }

        return result;
    }

    /// <summary>
    /// 列出所有已索引的工具名稱（供 system prompt 摘要用）。
    /// </summary>
    public List<string> ListAllNames()
    {
        return _entries.Select(e => e.Name).ToList();
    }

    private static double ComputeRelevance(List<string> queryTokens, ToolIndexEntry entry)
    {
        var nameTokens = entry.NameTokens;
        var descTokens = entry.DescTokens;

        double score = 0;
        foreach (var qt in queryTokens)
        {
            if (entry.Name.Contains(qt, StringComparison.OrdinalIgnoreCase))
            {
                // 名稱直接包含（最高權重，不再重複計分 token 匹配）
                score += 3.0;
            }
            else if (nameTokens.Any(nt => nt.Contains(qt, StringComparison.OrdinalIgnoreCase)))
            {
                // 名稱 token 部分匹配
                score += 2.0;
            }

            // 描述 token 匹配（獨立計分）
            if (descTokens.Any(dt => dt.Contains(qt, StringComparison.OrdinalIgnoreCase)))
            {
                score += 1.0;
            }
        }

        // 正規化：除以 query token 數量，讓長短 query 的分數可比
        return score / queryTokens.Count;
    }

    private static List<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Split([' ', '_', '-', '.', ',', '(', ')', '/', ':', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2)
            .Select(t => t.ToLowerInvariant())
            .ToList();
    }
}

/// <summary>索引條目：工具名稱 + 描述 + 原始工具參照 + 預計算 tokens。</summary>
public sealed record ToolIndexEntry(string Name, string Description, AITool Tool, List<string> NameTokens, List<string> DescTokens);

/// <summary>搜尋結果：工具名稱 + 描述 + 相關分數。</summary>
public sealed record ToolSearchResult(string Name, string Description, double Score);
