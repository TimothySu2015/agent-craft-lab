using System.ComponentModel;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 工具類工具實作（Calculator, DateTime, UUID, JSON Parser）。
/// </summary>
internal static partial class ToolImplementations
{
    [Description("取得目前的日期與時間，包含時區資訊")]
    internal static string GetCurrentDateTime()
    {
        var now = DateTimeOffset.Now;
        return $"Current date and time: {now:yyyy-MM-dd HH:mm:ss} (UTC{now.Offset:hh\\:mm})";
    }

    [Description("計算數學表達式，支援加減乘除與括號，例如 (3+5)*2")]
    internal static string Calculate(
        [Description("數學表達式，例如 (100+200)*0.05 或 2^10（冪運算）")] string expression)
    {
        try
        {
            // 支援冪運算：2^10 → Math.Pow(2,10)
            var normalized = Regex.Replace(expression, @"(\d+(?:\.\d+)?)\s*\^\s*(\d+(?:\.\d+)?)",
                m => Math.Pow(double.Parse(m.Groups[1].Value), double.Parse(m.Groups[2].Value)).ToString());

            if (!Regex.IsMatch(normalized, @"^[\d\s\+\-\*\/\%\.\(\)]+$"))
            {
                return $"Invalid expression: only numbers and operators (+, -, *, /, %, ^, parentheses) are allowed.";
            }

            var result = new DataTable().Compute(normalized, null);
            return $"{expression} = {result}";
        }
        catch (Exception ex)
        {
            return $"Calculation error: {ex.Message}";
        }
    }

    [Description("產生一個唯一的 UUID (GUID)")]
    internal static string GenerateUuid()
    {
        return Guid.NewGuid().ToString();
    }

    [Description("解析 JSON 字串，可選擇提取指定路徑的值。路徑格式如 'data.items[0].name'")]
    internal static string JsonParse(
        [Description("要解析的 JSON 字串")] string jsonString,
        [Description("要提取的 JSON 路徑，例如 'name' 或 'data.items[0].name'。留空則格式化整個 JSON")] string path = "")
    {
        try
        {
            var doc = JsonDocument.Parse(jsonString);

            if (string.IsNullOrWhiteSpace(path))
            {
                return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            }

            var current = doc.RootElement;
            var segments = Regex.Split(path, @"\.(?![^\[]*\])");

            foreach (var segment in segments)
            {
                var arrayMatch = Regex.Match(segment, @"^(.+?)\[(\d+)\]$");
                if (arrayMatch.Success)
                {
                    var propName = arrayMatch.Groups[1].Value;
                    var index = int.Parse(arrayMatch.Groups[2].Value);
                    current = current.GetProperty(propName)[index];
                }
                else
                {
                    current = current.GetProperty(segment);
                }
            }

            return current.ValueKind == JsonValueKind.String
                ? current.GetString() ?? ""
                : JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"JSON parse error: {ex.Message}";
        }
    }
}
