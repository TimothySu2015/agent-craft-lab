using AgentCraftLab.Cleaner.Elements;
using AgentCraftLab.Cleaner.Rules;

namespace AgentCraftLab.Tests.Cleaner;

public class CleaningRuleTests
{
    private static DocumentElement MakeElement(string text, ElementType type = ElementType.NarrativeText) =>
        new() { Type = type, Text = text, FileName = "test.txt", Index = 0 };

    // ── CleanWhitespaceRule ──

    [Fact]
    public void CleanWhitespace_CollapsesMultipleSpaces()
    {
        var rule = new CleanWhitespaceRule();
        var el = MakeElement("Hello    world   test");
        rule.Apply(el);
        Assert.Equal("Hello world test", el.Text);
    }

    [Fact]
    public void CleanWhitespace_CollapsesMultipleNewlines()
    {
        var rule = new CleanWhitespaceRule();
        var el = MakeElement("Line1\n\n\n\nLine2");
        rule.Apply(el);
        Assert.Equal("Line1\n\nLine2", el.Text);
    }

    [Fact]
    public void CleanWhitespace_TrimsLeadingAndTrailing()
    {
        var rule = new CleanWhitespaceRule();
        var el = MakeElement("   Hello   ");
        rule.Apply(el);
        Assert.Equal("Hello", el.Text);
    }

    // ── CleanBulletsRule ──

    [Theory]
    [InlineData("• First item", "First item")]
    [InlineData("○ Second item", "Second item")]
    [InlineData("- Third item", "Third item")]
    [InlineData("* Fourth item", "Fourth item")]
    [InlineData("■ Fifth item", "Fifth item")]
    public void CleanBullets_RemovesBulletPrefix(string input, string expected)
    {
        var rule = new CleanBulletsRule();
        var el = MakeElement(input, ElementType.ListItem);
        rule.Apply(el);
        Assert.Equal(expected, el.Text);
    }

    [Fact]
    public void CleanBullets_SkipsNonListElements()
    {
        var rule = new CleanBulletsRule();
        var el = MakeElement("• Not a list", ElementType.Title);
        Assert.False(rule.ShouldApply(el));
    }

    // ── CleanOrderedBulletsRule ──

    [Theory]
    [InlineData("1. First", "First")]
    [InlineData("1) First", "First")]
    [InlineData("a. Sub item", "Sub item")]
    public void CleanOrderedBullets_RemovesNumberPrefix(string input, string expected)
    {
        var rule = new CleanOrderedBulletsRule();
        var el = MakeElement(input, ElementType.ListItem);
        rule.Apply(el);
        Assert.Equal(expected, el.Text);
    }

    // ── CleanDashesRule ──

    [Fact]
    public void CleanDashes_RemovesDecorativeDashes()
    {
        var rule = new CleanDashesRule();
        var el = MakeElement("Section ---------- End");
        rule.Apply(el);
        Assert.Equal("Section  End", el.Text);
    }

    [Fact]
    public void CleanDashes_KeepsShortDashes()
    {
        var rule = new CleanDashesRule();
        var el = MakeElement("hello-world");
        rule.Apply(el);
        Assert.Equal("hello-world", el.Text);
    }

    // ── CleanNonAsciiRule ──

    [Fact]
    public void CleanNonAscii_RemovesControlChars()
    {
        var rule = new CleanNonAsciiRule();
        var el = MakeElement("Hello\x00World\x01Test");
        rule.Apply(el);
        Assert.Equal("HelloWorldTest", el.Text);
    }

    [Fact]
    public void CleanNonAscii_PreservesCJK()
    {
        var rule = new CleanNonAsciiRule();
        var el = MakeElement("你好世界 Hello こんにちは 안녕하세요");
        rule.Apply(el);
        Assert.Equal("你好世界 Hello こんにちは 안녕하세요", el.Text);
    }

    [Fact]
    public void CleanNonAscii_PreservesNewlinesAndTabs()
    {
        var rule = new CleanNonAsciiRule();
        var el = MakeElement("Line1\nLine2\tTabbed");
        rule.Apply(el);
        Assert.Equal("Line1\nLine2\tTabbed", el.Text);
    }

    // ── UnicodeNormalizeRule ──

    [Fact]
    public void UnicodeNormalize_ReplacesSmartQuotes()
    {
        var rule = new UnicodeNormalizeRule();
        var el = MakeElement("\u201cHello\u201d \u2018world\u2019");
        rule.Apply(el);
        Assert.Equal("\"Hello\" 'world'", el.Text);
    }

    [Fact]
    public void UnicodeNormalize_RemovesZeroWidthSpaces()
    {
        var rule = new UnicodeNormalizeRule();
        var el = MakeElement("Hello\u200bWorld\ufeff");
        rule.Apply(el);
        Assert.Equal("HelloWorld", el.Text);
    }

    [Fact]
    public void UnicodeNormalize_ReplacesNbsp()
    {
        var rule = new UnicodeNormalizeRule();
        var el = MakeElement("Hello\u00a0World");
        rule.Apply(el);
        Assert.Equal("Hello World", el.Text);
    }

    // ── GroupBrokenParagraphsRule ──

    [Fact]
    public void GroupBrokenParagraphs_MergesTruncatedLines()
    {
        var rule = new GroupBrokenParagraphsRule();
        var el = MakeElement("This is a long sentence that was\ntruncated by the PDF extractor");
        rule.Apply(el);
        Assert.Equal("This is a long sentence that was truncated by the PDF extractor", el.Text);
    }

    [Fact]
    public void GroupBrokenParagraphs_KeepsSentenceBoundaries()
    {
        var rule = new GroupBrokenParagraphsRule();
        var el = MakeElement("First sentence.\nSecond sentence.");
        rule.Apply(el);
        Assert.Equal("First sentence.\nSecond sentence.", el.Text);
    }

    [Fact]
    public void GroupBrokenParagraphs_KeepsParagraphBreaks()
    {
        var rule = new GroupBrokenParagraphsRule();
        var el = MakeElement("Paragraph one.\n\nParagraph two.");
        rule.Apply(el);
        Assert.Equal("Paragraph one.\n\nParagraph two.", el.Text);
    }

    [Fact]
    public void GroupBrokenParagraphs_SkipsTitleElements()
    {
        var rule = new GroupBrokenParagraphsRule();
        var el = MakeElement("Title text", ElementType.Title);
        Assert.False(rule.ShouldApply(el));
    }

    // ── RemoveHeaderFooterFilter ──

    [Theory]
    [InlineData(ElementType.Header, false)]
    [InlineData(ElementType.Footer, false)]
    [InlineData(ElementType.PageNumber, false)]
    [InlineData(ElementType.PageBreak, false)]
    [InlineData(ElementType.Title, true)]
    [InlineData(ElementType.NarrativeText, true)]
    [InlineData(ElementType.Table, true)]
    public void RemoveHeaderFooterFilter_FiltersCorrectly(ElementType type, bool shouldKeep)
    {
        var filter = new RemoveHeaderFooterFilter();
        var el = MakeElement("test", type);
        Assert.Equal(shouldKeep, filter.ShouldKeep(el));
    }

    // ── Rule Ordering ──

    [Fact]
    public void Rules_HaveCorrectOrdering()
    {
        var rules = new object[]
        {
            new CleanNonAsciiRule(),
            new UnicodeNormalizeRule(),
            new CleanWhitespaceRule(),
            new CleanBulletsRule(),
            new CleanOrderedBulletsRule(),
            new CleanDashesRule(),
            new GroupBrokenParagraphsRule(),
        }.Cast<AgentCraftLab.Cleaner.Abstractions.ICleaningRule>().OrderBy(r => r.Order).ToList();

        Assert.Equal("unicode_normalize", rules[0].Name);
        Assert.Equal("clean_non_ascii_control", rules[1].Name);
        Assert.Equal("clean_whitespace", rules[2].Name);
        Assert.Equal("group_broken_paragraphs", rules[^1].Name);
    }
}
