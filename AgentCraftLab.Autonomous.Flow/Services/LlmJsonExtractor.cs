namespace AgentCraftLab.Autonomous.Flow.Services;

/// <summary>
/// 從 LLM 回覆文字中抽出 JSON 區塊 — 處理三種情境：
/// <list type="number">
///   <item>包在 <c>```json ... ```</c> fenced block 內</item>
///   <item>直接 <c>{ ... }</c> 物件</item>
///   <item>Fenced block 但 pattern 值本身含有 <c>```</c>（例如 code 節點的 transformPattern）</item>
/// </list>
/// 第 3 種是 FlowPlanner 的真實案例 — 簡單的 <c>IndexOf("```")</c> 會誤將字串值內的
/// backtick 當成 closing fence。本 helper 改用「上一字元是換行」或「尾端」條件判斷
/// closing fence 位置。
/// </summary>
public static class LlmJsonExtractor
{
    /// <summary>
    /// 從 LLM 回覆中抽出 JSON 字串；找不到時回傳 null。
    /// </summary>
    public static string? Extract(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        // 嘗試 ```json ... ``` 格式
        var jsonStart = text.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (jsonStart >= 0)
        {
            var newline = text.IndexOf('\n', jsonStart);
            if (newline < 0) return null;
            var contentStart = newline + 1;

            // 往後找「位於行首的 ```」作為 closing fence，跳過字串內嵌的 backticks
            var searchPos = contentStart;
            while (searchPos < text.Length)
            {
                var candidate = text.IndexOf("```", searchPos, StringComparison.Ordinal);
                if (candidate < 0) break;

                // closing fence 的條件：上一字元是換行，或 candidate 是字串結尾
                var isLineStart = candidate == 0 || text[candidate - 1] == '\n';
                if (isLineStart)
                {
                    return text[contentStart..candidate].Trim();
                }

                searchPos = candidate + 3; // 跳過這個 backtick，繼續找
            }
            // fenced block 沒找到合法的 closing，fallthrough 到 brace-based
        }

        // Fallback：第一個 { 到最後一個 }
        var firstBrace = text.IndexOf('{');
        var lastBrace = text.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return text[firstBrace..(lastBrace + 1)];
        }

        return null;
    }
}
