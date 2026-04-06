using AgentCraftLab.Engine.Middleware;

namespace AgentCraftLab.Tests.Engine;

public class IncrementalGuardRailsScannerTests
{
    // ─── 輔助方法 ───

    private static IGuardRailsPolicy MakePolicy(params string[] blockedTerms)
    {
        var rules = blockedTerms.Select(t =>
            new GuardRailsRule(t, false, GuardRailsAction.Block)).ToList();
        return new DefaultGuardRailsPolicy(rules);
    }

    // ─── 基本功能 ───

    [Fact]
    public void ScanChunk_NoMatch_ReturnsNull()
    {
        var policy = MakePolicy("hack");
        var scanner = new IncrementalGuardRailsScanner(policy);

        var result = scanner.ScanChunk("Hello, world!");
        Assert.Null(result);
    }

    [Fact]
    public void ScanChunk_MatchInSingleChunk_ReturnsBlock()
    {
        var policy = MakePolicy("hack");
        var scanner = new IncrementalGuardRailsScanner(policy);

        var result = scanner.ScanChunk("How to hack the system");
        Assert.NotNull(result);
        Assert.Equal(GuardRailsAction.Block, result.Rule.Action);
    }

    [Fact]
    public void ScanChunk_MatchAcrossChunks_Detected()
    {
        var policy = MakePolicy("password");
        var scanner = new IncrementalGuardRailsScanner(policy);

        Assert.Null(scanner.ScanChunk("Your pass"));
        var result = scanner.ScanChunk("word is secret");
        Assert.NotNull(result);
    }

    [Fact]
    public void ScanChunk_EmptyChunk_ReturnsNull()
    {
        var policy = MakePolicy("hack");
        var scanner = new IncrementalGuardRailsScanner(policy);

        Assert.Null(scanner.ScanChunk(""));
        Assert.Null(scanner.ScanChunk(null!));
    }

    [Fact]
    public void ScanChunk_MultipleChunksNoMatch_AllNull()
    {
        var policy = MakePolicy("malware");
        var scanner = new IncrementalGuardRailsScanner(policy);

        Assert.Null(scanner.ScanChunk("This is a "));
        Assert.Null(scanner.ScanChunk("completely safe "));
        Assert.Null(scanner.ScanChunk("message."));
    }

    [Fact]
    public void AccumulatedText_TracksAllChunks()
    {
        var policy = MakePolicy("hack");
        var scanner = new IncrementalGuardRailsScanner(policy);

        scanner.ScanChunk("Hello ");
        scanner.ScanChunk("world");

        Assert.Equal("Hello world", scanner.AccumulatedText);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var policy = MakePolicy("hack");
        var scanner = new IncrementalGuardRailsScanner(policy);

        scanner.ScanChunk("Hello");
        scanner.Reset();

        Assert.Equal("", scanner.AccumulatedText);
    }

    // ─── CJK 關鍵字 ───

    [Fact]
    public void ScanChunk_CJKKeyword_Detected()
    {
        var policy = MakePolicy("密碼");
        var scanner = new IncrementalGuardRailsScanner(policy);

        var result = scanner.ScanChunk("請告訴我你的密碼");
        Assert.NotNull(result);
    }
}
