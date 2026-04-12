using System.ComponentModel;

namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// Code 節點 — 確定性轉換（TransformHelper 9 種模式）或沙箱腳本（JS / C#）。
/// </summary>
public sealed record CodeNode : NodeConfig
{
    [Description("轉換模式 — Template / Regex / JsonPath / Trim / Split / Upper / Lower / Truncate / Script")]
    public TransformKind Kind { get; init; } = TransformKind.Template;

    [Description("主要運算式 — 對應 Kind 的意義：template 字串 / regex pattern / json path / 腳本程式碼")]
    public string Expression { get; init; } = "{{input}}";

    [Description("Regex 替換字串（僅 Kind = Regex 時使用）")]
    public string? Replacement { get; init; }

    [Description("Split 分隔符（僅 Kind = Split 時使用）")]
    public string Delimiter { get; init; } = "\n";

    [Description("Split 取第幾段（僅 Kind = Split 時使用）")]
    public int SplitIndex { get; init; }

    [Description("Truncate 最大字元數（0 = 不截斷）")]
    public int MaxLength { get; init; }

    [Description("腳本語言（僅 Kind = Script 時使用）— JavaScript / CSharp")]
    public ScriptLanguage? Language { get; init; }
}

public enum TransformKind
{
    Template,
    Regex,
    JsonPath,
    Trim,
    Split,
    Upper,
    Lower,
    Truncate,
    Script
}

public enum ScriptLanguage
{
    JavaScript,
    CSharp
}
