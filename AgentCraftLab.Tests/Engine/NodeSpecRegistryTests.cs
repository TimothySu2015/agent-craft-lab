using System.Reflection;
using System.Text.Json.Serialization;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

/// <summary>
/// 保證 <see cref="NodeSpecRegistry"/> 與 <see cref="NodeConfig"/> 強型別 schema 同步 —
/// 新增節點型別時若漏寫 spec，此測試會立刻 fail。
/// </summary>
public class NodeSpecRegistryTests
{
    [Fact]
    public void AllSpecs_HaveNonEmptyFields()
    {
        foreach (var spec in NodeSpecRegistry.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(spec.Type), $"Spec missing Type");
            Assert.False(string.IsNullOrWhiteSpace(spec.ShortName), $"Spec '{spec.Type}' missing ShortName");
            Assert.False(string.IsNullOrWhiteSpace(spec.Description), $"Spec '{spec.Type}' missing Description");
            Assert.False(string.IsNullOrWhiteSpace(spec.ExampleJson), $"Spec '{spec.Type}' missing ExampleJson");
        }
    }

    [Fact]
    public void AllSchemaNodeConfigDiscriminators_AreDocumented()
    {
        // 從 Schema.NodeConfig 的 [JsonDerivedType] attributes 拿到所有 discriminator 字串
        var polyAttrs = typeof(NodeConfig)
            .GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false)
            .ToList();

        Assert.NotEmpty(polyAttrs); // 先確保 Schema 有設定

        // Meta 節點（start / end）在 prompt 中不需要呈現給 LLM（系統自動產生）
        var metaDiscriminators = new HashSet<string> { NodeTypes.Start, NodeTypes.End };

        var expectedDiscriminators = polyAttrs
            .Select(a => a.TypeDiscriminator?.ToString() ?? "")
            .Where(d => !string.IsNullOrEmpty(d) && !metaDiscriminators.Contains(d))
            .ToHashSet();

        var documentedTypes = NodeSpecRegistry.All.Select(s => s.Type).ToHashSet();

        var missing = expectedDiscriminators.Except(documentedTypes).ToList();
        var extra = documentedTypes.Except(expectedDiscriminators).ToList();

        Assert.True(missing.Count == 0,
            $"NodeSpecRegistry 缺少以下節點型別（請新增 NodeSpec）：{string.Join(", ", missing)}");
        Assert.True(extra.Count == 0,
            $"NodeSpecRegistry 包含 Schema 不認識的節點型別（請刪除或修正）：{string.Join(", ", extra)}");
    }

    [Fact]
    public void BuildMarkdownSection_ContainsAllNodeTypeHeadings()
    {
        var md = NodeSpecRegistry.BuildMarkdownSection();

        foreach (var spec in NodeSpecRegistry.All)
        {
            Assert.Contains($"### {spec.Type}", md);
        }
    }

    [Fact]
    public void BuildMarkdownSection_ContainsJsonCodeFences()
    {
        var md = NodeSpecRegistry.BuildMarkdownSection();

        // 每個 spec 應產生一個 ```json ... ``` 區塊
        var jsonFenceCount = md.Split("```json").Length - 1;
        Assert.Equal(NodeSpecRegistry.All.Count, jsonFenceCount);
    }

    [Fact]
    public void AgentSpec_PreservesPlaceholdersForOuterReplacement()
    {
        // Agent spec 的 Notes 含 {PROVIDERS_SECTION}/{TOOLS_SECTION}/{SKILLS_SECTION} 外層 placeholder。
        // 這些 placeholder 必須在 BuildMarkdownSection 輸出中保留，供 FlowBuilderService
        // 後續 Replace 注入動態內容。
        var md = NodeSpecRegistry.BuildMarkdownSection();

        Assert.Contains("{PROVIDERS_SECTION}", md);
        Assert.Contains("{TOOLS_SECTION}", md);
        Assert.Contains("{SKILLS_SECTION}", md);
    }

    [Fact]
    public void NodeTypes_HaveKnownContent()
    {
        // 核心節點必須被記錄 — 這些是 LLM 最常用的型別
        var md = NodeSpecRegistry.BuildMarkdownSection();

        Assert.Contains(NodeTypes.Agent, md);
        Assert.Contains(NodeTypes.Condition, md);
        Assert.Contains(NodeTypes.Loop, md);
        Assert.Contains(NodeTypes.HttpRequest, md);
        Assert.Contains(NodeTypes.Parallel, md);
        Assert.Contains(NodeTypes.Iteration, md);
    }
}
