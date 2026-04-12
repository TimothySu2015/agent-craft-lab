using AgentCraftLab.Engine.Models.Schema;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

public class TransformHelperTests
{
    // ApplyTransform(transformType, input, template?, pattern?, replacement?, maxLength, delimiter, splitIndex)

    [Fact]
    public void Template_ReplacesInputPlaceholder()
    {
        var result = TransformHelper.ApplyTransform("template", "World", template: "Hello {{input}}");
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Template_NullTemplate_DefaultsToInput()
    {
        var result = TransformHelper.ApplyTransform("template", "test");
        Assert.Equal("test", result);
    }

    [Fact]
    public void RegexExtract_WithMatch()
    {
        var result = TransformHelper.ApplyTransform("regex-extract", "abc123def", pattern: @"\d+");
        Assert.Contains("123", result);
    }

    [Fact]
    public void RegexReplace_Basic()
    {
        var result = TransformHelper.ApplyTransform("regex-replace", "a1b2", pattern: @"\d", replacement: "X");
        Assert.Equal("aXbX", result);
    }

    [Fact]
    public void JsonPath_SimpleProperty()
    {
        var result = TransformHelper.ApplyTransform("json-path", "{\"name\":\"test\"}", pattern: "$.name");
        Assert.Equal("test", result);
    }

    [Fact]
    public void JsonPath_NotFound_ReturnsError()
    {
        var result = TransformHelper.ApplyTransform("json-path", "{\"a\":1}", pattern: "$.missing");
        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Trim_TruncatesLongInput()
    {
        var result = TransformHelper.ApplyTransform("trim", "1234567890", maxLength: 5);
        Assert.Equal(5, result.Length);
    }

    [Fact]
    public void Trim_ShortInput_NoChange()
    {
        var result = TransformHelper.ApplyTransform("trim", "abc", maxLength: 100);
        Assert.Equal("abc", result);
    }

    [Fact]
    public void Upper_Converts()
    {
        var result = TransformHelper.ApplyTransform("upper", "hello");
        Assert.Equal("HELLO", result);
    }

    [Fact]
    public void Lower_Converts()
    {
        var result = TransformHelper.ApplyTransform("lower", "HELLO");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void SplitTake_DefaultNewline()
    {
        var result = TransformHelper.ApplyTransform("split-take", "a\nb\nc");
        Assert.Equal("a", result);
    }

    [Fact]
    public void UnknownType_ReturnsInput()
    {
        var result = TransformHelper.ApplyTransform("unknown-type-xyz", "test");
        Assert.Equal("test", result);
    }

    [Fact]
    public void Template_MultipleReplacements()
    {
        var result = TransformHelper.ApplyTransform("template", "World", template: "{{input}} says {{input}}");
        Assert.Equal("World says World", result);
    }

    [Fact]
    public void RegexExtract_NoMatch_ReturnsEmpty()
    {
        var result = TransformHelper.ApplyTransform("regex-extract", "hello", pattern: @"\d+");
        Assert.Equal("", result);
    }

    [Fact]
    public void RegexReplace_EmptyPattern_ReturnsInput()
    {
        var result = TransformHelper.ApplyTransform("regex-replace", "hello", pattern: "", replacement: "X");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void JsonPath_NestedProperty()
    {
        var result = TransformHelper.ApplyTransform("json-path", "{\"a\":{\"b\":\"deep\"}}", pattern: "$.a.b");
        Assert.Equal("deep", result);
    }

    [Fact]
    public void JsonPath_InvalidJson_ReturnsError()
    {
        var result = TransformHelper.ApplyTransform("json-path", "not json", pattern: "$.x");
        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Trim_ZeroMaxLength_NoChange()
    {
        var result = TransformHelper.ApplyTransform("trim", "hello", maxLength: 0);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void SplitTake_CustomDelimiter()
    {
        var result = TransformHelper.ApplyTransform("split-take", "a,b,c", delimiter: ",", splitIndex: 1);
        Assert.Equal("b", result);
    }

    [Fact]
    public void SplitTake_IndexOutOfRange_ReturnsInput()
    {
        var result = TransformHelper.ApplyTransform("split-take", "a,b", delimiter: ",", splitIndex: 99);
        Assert.Equal("a,b", result);
    }

    [Fact]
    public void Upper_EmptyString()
    {
        var result = TransformHelper.ApplyTransform("upper", "");
        Assert.Equal("", result);
    }

    // ════════════════════════════════════════
    // Handlebars {{#each}} 支援
    // ════════════════════════════════════════

    [Fact]
    public void Template_Each_JsonArray()
    {
        var input = "[{\"name\":\"SK-II\",\"product\":\"Pitera\"},{\"name\":\"Shiseido\",\"product\":\"Ultimune\"}]";
        var template = "| Name | Product |\n|---|---|\n{{#each input}}| {{this.name}} | {{this.product}} |\n{{/each}}";
        var result = TransformHelper.ApplyTransform("template", input, template: template);
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
        var template = "{{#each input}}{{@index}}. {{this.name}}\n{{/each}}";
        var result = TransformHelper.ApplyTransform("template", input, template: template);
        Assert.Contains("0. A", result);
        Assert.Contains("1. B", result);
    }

    [Fact]
    public void Template_Each_InvalidJson_Fallback()
    {
        var result = TransformHelper.ApplyTransform("template", "not json", template: "before {{input}} {{#each input}}{{this.x}}{{/each}}");
        // JSON 解析失敗 → fallback 到 {{input}} 替換
        Assert.Contains("not json", result);
    }

    [Fact]
    public void Template_Each_NonArray_Fallback()
    {
        var result = TransformHelper.ApplyTransform("template", "{\"a\":1}", template: "data: {{input}} {{#each input}}x{{/each}}");
        // 非 array → fallback 到 {{input}} 替換
        Assert.Contains("{\"a\":1}", result);
    }

    [Fact]
    public void Template_Each_NoInputPlaceholder_Fallback_ReturnsInput()
    {
        // template 只有 {{#each}} 沒有 {{input}}，JSON 解析失敗時應回傳 input 資料（非原始 template）
        var input = "some non-json data from parallel merge";
        var template = "| Name | Value |\n|---|---|\n{{#each input}}| {{this.name}} | {{this.value}} |\n{{/each}}";
        var result = TransformHelper.ApplyTransform("template", input, template: template);
        Assert.Equal(input, result);
        Assert.DoesNotContain("{{#each", result);
    }

    [Fact]
    public void Template_Each_NonArray_NoInputPlaceholder_ReturnsInput()
    {
        // 非 array + 無 {{input}} → 回傳 input 資料
        var input = "{\"name\":\"test\"}";
        var template = "{{#each input}}| {{this.x}} |\n{{/each}}";
        var result = TransformHelper.ApplyTransform("template", input, template: template);
        Assert.Equal(input, result);
    }

    [Fact]
    public void Template_Each_ExtractsJsonArrayFromWrappedText()
    {
        // LLM 常見輸出：說明文字 + ```json [...] ``` + 後記
        var input = "以下是結果：\n\n```json\n[{\"name\":\"SK-II\",\"product\":\"Pitera\"},{\"name\":\"Shiseido\",\"product\":\"Ultimune\"}]\n```\n\n以上均為日本品牌。";
        var template = "| Name | Product |\n|---|---|\n{{#each input}}| {{this.name}} | {{this.product}} |\n{{/each}}";
        var result = TransformHelper.ApplyTransform("template", input, template: template);
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
        var template = "{{#each input}}| {{this.english_name}} | {{this.japanese_name}} |\n{{/each}}";
        var result = TransformHelper.ApplyTransform("template", input, template: template);
        Assert.Contains("POLA", result);
        Assert.Contains("ポーラ", result);
        Assert.DoesNotContain("{{this.", result);
    }

    [Fact]
    public void Template_Each_FuzzyMatchPascalCase()
    {
        // template 用 snake_case，JSON 用 PascalCase
        var input = "[{\"EnglishName\":\"DHC\",\"ProductName\":\"Oil\"}]";
        var template = "{{#each input}}{{this.english_name}}: {{this.product_name}}\n{{/each}}";
        var result = TransformHelper.ApplyTransform("template", input, template: template);
        Assert.Contains("DHC", result);
        Assert.Contains("Oil", result);
        Assert.DoesNotContain("{{this.", result);
    }

    // ════════════════════════════════════════
    // TransformKind enum overload
    // ════════════════════════════════════════

    [Fact]
    public void EnumOverload_Template_ReturnsTemplateResult()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Template, "hello", "Result: {{input}}");
        Assert.Equal("Result: hello", result);
    }

    [Fact]
    public void EnumOverload_Regex_ReturnsReplacedResult()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Regex, "foo bar", "foo", replacement: "baz");
        Assert.Equal("baz bar", result);
    }

    [Fact]
    public void EnumOverload_JsonPath_ExtractsValue()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.JsonPath, "{\"name\":\"John\"}", "name");
        Assert.Equal("John", result);
    }

    [Fact]
    public void EnumOverload_Trim_TruncatesLongInput()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Trim, "abcdefgh", maxLength: 3);
        Assert.Equal("abc", result);
    }

    [Fact]
    public void EnumOverload_Truncate_SameBehaviorAsTrim()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Truncate, "abcdefgh", maxLength: 5);
        Assert.Equal("abcde", result);
    }

    [Fact]
    public void EnumOverload_Split_TakesNthSegment()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Split, "a\nb\nc", delimiter: "\n", splitIndex: 1);
        Assert.Equal("b", result);
    }

    [Fact]
    public void EnumOverload_Upper_UpperCases()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Upper, "hello");
        Assert.Equal("HELLO", result);
    }

    [Fact]
    public void EnumOverload_Lower_LowerCases()
    {
        var result = TransformHelper.ApplyTransform(TransformKind.Lower, "HELLO");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void EnumOverload_UnknownKind_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TransformHelper.ApplyTransform((TransformKind)999, "x"));
    }
}
