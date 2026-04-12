using AgentCraftLab.Engine.Models.Schema;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

public class TransformHelperTests
{
    [Fact]
    public void Template_ReplacesInputPlaceholder()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Template, "World", expression: "Hello {{input}}");
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Template_NullExpression_DefaultsToInput()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Template, "test");
        Assert.Equal("test", result);
    }

    [Fact]
    public void RegexExtract_WithMatch()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Regex, "abc123def", expression: @"\d+");
        Assert.Contains("123", result);
    }

    [Fact]
    public void RegexReplace_Basic()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Regex, "a1b2", expression: @"\d", replacement: "X");
        Assert.Equal("aXbX", result);
    }

    [Fact]
    public void JsonPath_SimpleProperty()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.JsonPath, "{\"name\":\"test\"}", expression: "$.name");
        Assert.Equal("test", result);
    }

    [Fact]
    public void JsonPath_NotFound_ReturnsError()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.JsonPath, "{\"a\":1}", expression: "$.missing");
        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Trim_TruncatesLongInput()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Trim, "1234567890", maxLength: 5);
        Assert.Equal(5, result.Length);
    }

    [Fact]
    public void Trim_ShortInput_NoChange()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Trim, "abc", maxLength: 100);
        Assert.Equal("abc", result);
    }

    [Fact]
    public void Upper_Converts()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Upper, "hello");
        Assert.Equal("HELLO", result);
    }

    [Fact]
    public void Lower_Converts()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Lower, "HELLO");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void SplitTake_DefaultNewline()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Split, "a\nb\nc");
        Assert.Equal("a", result);
    }

    [Fact]
    public void UnknownKind_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TransformHelper.ApplyTransform((TransformKind)999, "test"));
    }

    [Fact]
    public void Template_MultipleReplacements()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Template, "World", expression: "{{input}} says {{input}}");
        Assert.Equal("World says World", result);
    }

    [Fact]
    public void RegexExtract_NoMatch_ReturnsEmpty()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Regex, "hello", expression: @"\d+");
        Assert.Equal("", result);
    }

    [Fact]
    public void RegexReplace_EmptyPattern_ReturnsInput()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Regex, "hello", expression: "", replacement: "X");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void JsonPath_NestedProperty()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.JsonPath, "{\"a\":{\"b\":\"deep\"}}", expression: "$.a.b");
        Assert.Equal("deep", result);
    }

    [Fact]
    public void JsonPath_InvalidJson_ReturnsError()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.JsonPath, "not json", expression: "$.x");
        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Trim_ZeroMaxLength_NoChange()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Trim, "hello", maxLength: 0);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void SplitTake_CustomDelimiter()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Split, "a,b,c", delimiter: ",", splitIndex: 1);
        Assert.Equal("b", result);
    }

    [Fact]
    public void SplitTake_IndexOutOfRange_ReturnsInput()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Split, "a,b", delimiter: ",", splitIndex: 99);
        Assert.Equal("a,b", result);
    }

    [Fact]
    public void Upper_EmptyString()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Upper, "");
        Assert.Equal("", result);
    }

    // ════════════════════════════════════════
    // Handlebars {{#each}} 支援
    // ════════════════════════════════════════

    [Fact]
    public void Template_Each_JsonArray()
    {
        var input = "[{\"name\":\"SK-II\",\"product\":\"Pitera\"},{\"name\":\"Shiseido\",\"product\":\"Ultimune\"}]";
        var expression = "| Name | Product |\n|---|---|\n{{#each input}}| {{this.name}} | {{this.product}} |\n{{/each}}";
        var result = TransformHelper.ApplyTransform(TransformKind.Template, input, expression: expression);
        Assert.Contains("SK-II", result);
        Assert.Contains("Pitera", result);
        Assert.Contains("Shiseido", result);
        Assert.Contains("Ultimune", result);
        Assert.DoesNotContain("{{#each", result);
    }

    [Fact]
    public void Template_Each_WithIndex()
    {
        var input = "[{\"name\":\"A\"},{\"name\":\"B\"}]";
        var expression = "{{#each input}}{{@index}}. {{this.name}}\n{{/each}}";
        var result = TransformHelper.ApplyTransform(TransformKind.Template, input, expression: expression);
        Assert.Contains("0. A", result);
        Assert.Contains("1. B", result);
    }

    [Fact]
    public void Template_Each_InvalidJson_Fallback()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Template, "not json", expression: "before {{input}} {{#each input}}{{this.x}}{{/each}}");
        // JSON 解析失敗 → fallback 到 {{input}} 替換
        Assert.Contains("not json", result);
    }

    [Fact]
    public void Template_Each_NonArray_Fallback()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Template, "{\"a\":1}", expression: "data: {{input}} {{#each input}}x{{/each}}");
        // 非 array → fallback 到 {{input}} 替換
        Assert.Contains("{\"a\":1}", result);
    }

    [Fact]
    public void Template_Each_NoInputPlaceholder_Fallback_ReturnsInput()
    {
        // template 只有 {{#each}} 沒有 {{input}}，JSON 解析失敗時應回傳 input 資料（非原始 template）
        var input = "some non-json data from parallel merge";
        var expression = "| Name | Value |\n|---|---|\n{{#each input}}| {{this.name}} | {{this.value}} |\n{{/each}}";
        var result = TransformHelper.ApplyTransform(TransformKind.Template, input, expression: expression);
        Assert.Equal(input, result);
        Assert.DoesNotContain("{{#each", result);
    }

    [Fact]
    public void Template_Each_NonArray_NoInputPlaceholder_ReturnsInput()
    {
        // 非 array + 無 {{input}} → 回傳 input 資料
        var input = "{\"name\":\"test\"}";
        var expression = "{{#each input}}| {{this.x}} |\n{{/each}}";
        var result = TransformHelper.ApplyTransform(TransformKind.Template, input, expression: expression);
        Assert.Equal(input, result);
    }

    [Fact]
    public void Template_Each_ExtractsJsonArrayFromWrappedText()
    {
        // LLM 常見輸出：說明文字 + ```json [...] ``` + 後記
        var input = "以下是結果：\n\n```json\n[{\"name\":\"SK-II\",\"product\":\"Pitera\"},{\"name\":\"Shiseido\",\"product\":\"Ultimune\"}]\n```\n\n以上均為日本品牌。";
        var expression = "| Name | Product |\n|---|---|\n{{#each input}}| {{this.name}} | {{this.product}} |\n{{/each}}";
        var result = TransformHelper.ApplyTransform(TransformKind.Template, input, expression: expression);
        Assert.Contains("SK-II", result);
        Assert.Contains("Pitera", result);
        Assert.Contains("Shiseido", result);
        Assert.Contains("Ultimune", result);
        Assert.DoesNotContain("{{#each", result);
        Assert.Contains("| Name | Product |", result);
    }

    [Fact]
    public void Template_Each_FuzzyMatchPropertyNames()
    {
        // template 用 snake_case，JSON 用 camelCase — fuzzy match 應成功
        var input = "[{\"englishName\":\"POLA\",\"japaneseName\":\"ポーラ\"}]";
        var expression = "{{#each input}}| {{this.english_name}} | {{this.japanese_name}} |\n{{/each}}";
        var result = TransformHelper.ApplyTransform(TransformKind.Template, input, expression: expression);
        Assert.Contains("POLA", result);
        Assert.Contains("ポーラ", result);
        Assert.DoesNotContain("{{this.", result);
    }

    [Fact]
    public void Template_Each_FuzzyMatchPascalCase()
    {
        // template 用 snake_case，JSON 用 PascalCase
        var input = "[{\"EnglishName\":\"DHC\",\"ProductName\":\"Oil\"}]";
        var expression = "{{#each input}}{{this.english_name}}: {{this.product_name}}\n{{/each}}";
        var result = TransformHelper.ApplyTransform(TransformKind.Template, input, expression: expression);
        Assert.Contains("DHC", result);
        Assert.Contains("Oil", result);
        Assert.DoesNotContain("{{this.", result);
    }

    // ════════════════════════════════════════
    // Truncate (alias for Trim)
    // ════════════════════════════════════════

    [Fact]
    public void Truncate_SameBehaviorAsTrim()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Truncate, "abcdefgh", maxLength: 5);
        Assert.Equal("abcde", result);
    }
}
