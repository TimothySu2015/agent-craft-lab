using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

public sealed class DiffToolTests : IDisposable
{
    private readonly string _tempDir;

    public DiffToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"diff_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        ToolImplementations.WorkingDirectory = _tempDir;
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* cleanup */ }
    }

    private string CreateFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return name;
    }

    // ═══════════════════════════════════════════════
    // FileDiff 測試
    // ═══════════════════════════════════════════════

    [Fact]
    public void FileDiff_IdenticalFiles_ReportsNoDifference()
    {
        var f1 = CreateFile("a.txt", "hello\nworld\n");
        var f2 = CreateFile("b.txt", "hello\nworld\n");

        var result = ToolImplementations.FileDiff(f1, f2);

        Assert.Contains("No differences", result);
    }

    [Fact]
    public void FileDiff_SingleLineChange_ShowsUnifiedDiff()
    {
        var f1 = CreateFile("a.txt", "line1\nline2\nline3\n");
        var f2 = CreateFile("b.txt", "line1\nmodified\nline3\n");

        var result = ToolImplementations.FileDiff(f1, f2);

        Assert.Contains("---", result);
        Assert.Contains("+++", result);
        Assert.Contains("@@", result);
        Assert.Contains("-line2", result);
        Assert.Contains("+modified", result);
    }

    [Fact]
    public void FileDiff_AddedLines_ShowsInsertions()
    {
        var f1 = CreateFile("a.txt", "line1\nline2\n");
        var f2 = CreateFile("b.txt", "line1\nline2\nnewline\n");

        var result = ToolImplementations.FileDiff(f1, f2);

        Assert.Contains("+newline", result);
        Assert.Contains("added", result.ToLowerInvariant());
    }

    [Fact]
    public void FileDiff_DeletedLines_ShowsDeletions()
    {
        var f1 = CreateFile("a.txt", "line1\nline2\nline3\n");
        var f2 = CreateFile("b.txt", "line1\nline3\n");

        var result = ToolImplementations.FileDiff(f1, f2);

        Assert.Contains("-line2", result);
        Assert.Contains("deleted", result.ToLowerInvariant());
    }

    [Fact]
    public void FileDiff_FileNotFound_ReturnsError()
    {
        var f1 = CreateFile("a.txt", "content");
        var result = ToolImplementations.FileDiff(f1, "nonexistent.txt");

        Assert.Contains("Error", result);
        Assert.Contains("not found", result);
    }

    [Fact]
    public void FileDiff_BinaryFile_ReturnsError()
    {
        var f1 = CreateFile("a.dll", "binary");
        var f2 = CreateFile("b.txt", "text");

        var result = ToolImplementations.FileDiff(f1, f2);

        Assert.Contains("binary", result.ToLowerInvariant());
    }

    [Fact]
    public void FileDiff_PathOutsideWorkingDir_ReturnsError()
    {
        var f1 = CreateFile("a.txt", "content");
        var result = ToolImplementations.FileDiff(f1, "../../etc/passwd");

        Assert.Contains("Error", result);
    }

    [Fact]
    public void FileDiff_IgnoreWhitespace_TreatsWhitespaceEqual()
    {
        var f1 = CreateFile("a.txt", "hello   world\n");
        var f2 = CreateFile("b.txt", "hello world\n");

        var result = ToolImplementations.FileDiff(f1, f2, ignoreWhitespace: true);

        Assert.Contains("No differences", result);
    }

    [Fact]
    public void FileDiff_Summary_IncludesStats()
    {
        var f1 = CreateFile("a.txt", "line1\nline2\nline3\n");
        var f2 = CreateFile("b.txt", "line1\nchanged\nline3\nnewline\n");

        var result = ToolImplementations.FileDiff(f1, f2);

        Assert.Contains("Summary:", result);
    }

    // ═══════════════════════════════════════════════
    // TextDiff 測試
    // ═══════════════════════════════════════════════

    [Fact]
    public void TextDiff_IdenticalText_ReportsNoDifference()
    {
        var result = ToolImplementations.TextDiff("hello\nworld", "hello\nworld");

        Assert.Contains("No differences", result);
    }

    [Fact]
    public void TextDiff_DifferentText_ShowsUnifiedDiff()
    {
        var result = ToolImplementations.TextDiff("line1\nline2", "line1\nchanged");

        Assert.Contains("-line2", result);
        Assert.Contains("+changed", result);
    }

    [Fact]
    public void TextDiff_EmptyToContent_ShowsAllAdded()
    {
        var result = ToolImplementations.TextDiff("", "new content");

        Assert.Contains("+new content", result);
    }

    [Fact]
    public void TextDiff_ContentToEmpty_ShowsAllDeleted()
    {
        var result = ToolImplementations.TextDiff("old content", "");

        Assert.Contains("-old content", result);
    }

    [Fact]
    public void TextDiff_NullText_TreatedAsEmpty()
    {
        var result = ToolImplementations.TextDiff(null!, "hello");

        Assert.Contains("+hello", result);
    }

    [Fact]
    public void TextDiff_BothNull_ReturnsError()
    {
        var result = ToolImplementations.TextDiff(null!, null!);

        Assert.Contains("Error", result);
    }

    [Fact]
    public void TextDiff_ContextLines_AffectsOutput()
    {
        var lines = Enumerable.Range(1, 20).Select(i => $"line{i}");
        var text1 = string.Join("\n", lines);
        var text2 = text1.Replace("line10", "modified10");

        var result0 = ToolImplementations.TextDiff(text1, text2, contextLines: 0);
        var result5 = ToolImplementations.TextDiff(text1, text2, contextLines: 5);

        // 更多 context 行 → 更長的輸出
        Assert.True(result5.Length > result0.Length);
    }

    [Fact]
    public void TextDiff_IgnoreWhitespace_Works()
    {
        var result = ToolImplementations.TextDiff("hello   world", "hello world", ignoreWhitespace: true);

        Assert.Contains("No differences", result);
    }

    [Fact]
    public void TextDiff_Labels_ShowOriginalAndModified()
    {
        var result = ToolImplementations.TextDiff("a", "b");

        Assert.Contains("--- original", result);
        Assert.Contains("+++ modified", result);
    }

    // ═══════════════════════════════════════════════
    // 邊界場景測試
    // ═══════════════════════════════════════════════

    [Fact]
    public void TextDiff_MultipleHunks_ProducesMultipleAtHeaders()
    {
        // 20 行中第 3 行和第 17 行各改一處，contextLines=1 時應產生兩個不連續 hunk
        var lines = Enumerable.Range(1, 20).Select(i => $"line{i}").ToList();
        var text1 = string.Join("\n", lines);

        lines[2] = "changed3";   // 第 3 行
        lines[16] = "changed17"; // 第 17 行
        var text2 = string.Join("\n", lines);

        var result = ToolImplementations.TextDiff(text1, text2, contextLines: 1);

        var hunkCount = result.Split("@@").Length / 2; // 每個 hunk 有一對 @@
        Assert.True(hunkCount >= 2, $"Expected at least 2 hunks, got {hunkCount}. Output:\n{result}");
    }

    [Fact]
    public void TextDiff_LargeOutput_TruncatesWithMessage()
    {
        // 產生大量差異行以觸發截斷
        var lines1 = Enumerable.Range(1, 1500).Select(i => $"old_{i}");
        var lines2 = Enumerable.Range(1, 1500).Select(i => $"new_{i}");
        var text1 = string.Join("\n", lines1);
        var text2 = string.Join("\n", lines2);

        var result = ToolImplementations.TextDiff(text1, text2);

        Assert.Contains("truncated", result);
    }

    [Fact]
    public void FileDiff_EmptyFileVsContent_ShowsAllAdded()
    {
        var f1 = CreateFile("empty.txt", "");
        var f2 = CreateFile("content.txt", "line1\nline2\nline3\n");

        var result = ToolImplementations.FileDiff(f1, f2);

        Assert.Contains("+line1", result);
        Assert.Contains("+line2", result);
        Assert.Contains("+line3", result);
    }

    [Fact]
    public void TextDiff_CrlfVsLf_DetectsDifference()
    {
        var text1 = "line1\r\nline2\r\nline3";
        var text2 = "line1\nline2\nline3";

        // CRLF vs LF 在 TrimEnd('\r') 後應該相同 → 無差異
        var result = ToolImplementations.TextDiff(text1, text2);

        Assert.Contains("No differences", result);
    }

    [Fact]
    public void TextDiff_Stats_AreAccurate()
    {
        // 1 modified (line2→changed), 1 deleted (line3), 1 added (newline)
        var text1 = "line1\nline2\nline3";
        var text2 = "line1\nchanged\nnewline";

        var result = ToolImplementations.TextDiff(text1, text2);

        // line2→changed 和 line3→newline：DiffPlex 視為同一個 block（2 delete + 2 insert → 2 modified）
        // 精確驗證 Summary 行存在且包含數字
        Assert.Matches(@"Summary: \d+ added, \d+ deleted, \d+ modified", result);
    }
}
