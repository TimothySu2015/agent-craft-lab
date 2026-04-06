using System.Text;
using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.Rules;

/// <summary>
/// 合併被 PDF 換行截斷的段落。
/// PDF 擷取常把同一段落拆成多行（每行結尾有 \n），
/// 此規則將不以句末標點結尾的行合併回同一段落。
/// 對應 Unstructured 的 group_broken_paragraphs。
/// </summary>
public sealed class GroupBrokenParagraphsRule : ICleaningRule
{
    public string Name => "group_broken_paragraphs";
    public int Order => 400;

    // CJK 與西文的句末標點
    private static readonly char[] SentenceEnders = ['.', '!', '?', '。', '！', '？', '：', '；'];

    public bool ShouldApply(DocumentElement element) =>
        element.Type is ElementType.NarrativeText or ElementType.UncategorizedText;

    public void Apply(DocumentElement element)
    {
        var lines = element.Text.Split('\n');
        if (lines.Length <= 1)
        {
            return;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                // 空行 = 段落分隔
                sb.Append("\n\n");
                continue;
            }

            sb.Append(line);

            if (i < lines.Length - 1)
            {
                var nextLine = lines[i + 1].TrimStart();
                if (string.IsNullOrWhiteSpace(nextLine))
                {
                    // 下一行是空行，不合併
                    continue;
                }

                // 本行以句末標點結尾 → 不合併（段落結束）
                if (line.Length > 0 && SentenceEnders.Contains(line[^1]))
                {
                    sb.Append('\n');
                }
                else
                {
                    // 被 PDF 截斷的行 → 用空格合併
                    sb.Append(' ');
                }
            }
        }

        element.Text = sb.ToString().Trim();
    }
}
