using AgentCraftLab.Engine.Services;
using Xunit.Abstractions;

namespace AgentCraftLab.Tests.Engine;

/// <summary>
/// 診斷 test — 把 NodeSpecRegistry 產出的 markdown 寫到 .ai_docs 供人工比對。
/// 這不是 regression test，而是方便驗證 Phase D 改動前後 prompt 內容是否一致。
/// </summary>
public class NodeSpecRegistryDumpTest
{
    private readonly ITestOutputHelper _output;

    public NodeSpecRegistryDumpTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DumpRenderedNodeSpecs_ToFile()
    {
        var markdown = NodeSpecRegistry.BuildMarkdownSection();

        // 找到 repo root（向上搜尋 .git 資料夾）
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            _output.WriteLine("[DumpTest] 找不到 repo root，改輸出到 AppContext.BaseDirectory");
            dir = new DirectoryInfo(AppContext.BaseDirectory);
        }

        var outputDir = Path.Combine(dir.FullName, ".ai_docs");
        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, "generated-node-specs.md");

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var fullContent = $$"""
            <!-- 此檔案由 NodeSpecRegistryDumpTest 產生，方便人工比對 Phase D 前後 prompt -->
            <!-- 產生時間：{{timestamp}} UTC -->

            # AI Build Prompt - 節點類型規格段落

            以下是 `FlowBuilderService` 注入到 LLM system prompt 的 `{NODE_SPECS}` 段落，
            由 `NodeSpecRegistry.BuildMarkdownSection()` 產生。

            注意：`{PROVIDERS_SECTION}` / `{TOOLS_SECTION}` / `{SKILLS_SECTION}` 會由
            `FlowBuilderService` constructor 在之後的 Replace 步驟中注入實際內容（Providers、
            註冊的工具、可用 Skills）。

            ---

            {{markdown}}
            """;

        File.WriteAllText(outputPath, fullContent);

        _output.WriteLine($"[DumpTest] 已寫入 {outputPath}");
        _output.WriteLine($"[DumpTest] 內容長度 {fullContent.Length} 字元");
        _output.WriteLine($"[DumpTest] 節點數 {NodeSpecRegistry.All.Count}");

        Assert.True(File.Exists(outputPath));
        Assert.True(fullContent.Length > 0);
    }
}
