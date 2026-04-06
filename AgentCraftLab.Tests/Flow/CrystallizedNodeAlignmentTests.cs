using System.Reflection;
using AgentCraftLab.Autonomous.Flow.Services;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Tests.Flow;

/// <summary>
/// 驗證 CrystallizedNode/Connection 的欄位名與 Engine WorkflowNode/Connection 一致。
/// 防止未來修改時欄位名漂移 — 如果新增欄位忘記對齊，測試會失敗。
/// </summary>
public sealed class CrystallizedNodeAlignmentTests
{
    /// <summary>
    /// CrystallizedNode 的每個屬性名都必須存在於 WorkflowNode 中（case-insensitive）。
    /// </summary>
    [Fact]
    public void CrystallizedNode_AllProperties_ExistInWorkflowNode()
    {
        var workflowNodeProps = typeof(WorkflowNode)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name.ToLowerInvariant())
            .ToHashSet();

        var crystallizedProps = typeof(CrystallizedNode)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in crystallizedProps)
        {
            Assert.True(
                workflowNodeProps.Contains(prop.Name.ToLowerInvariant()),
                $"CrystallizedNode.{prop.Name} has no matching property in WorkflowNode. " +
                $"Available: {string.Join(", ", workflowNodeProps.Order())}");
        }
    }

    /// <summary>
    /// CrystallizedConnection 的每個屬性名都必須存在於 WorkflowConnection 中。
    /// </summary>
    [Fact]
    public void CrystallizedConnection_AllProperties_ExistInWorkflowConnection()
    {
        var workflowConnProps = typeof(WorkflowConnection)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name.ToLowerInvariant())
            .ToHashSet();

        var crystallizedProps = typeof(CrystallizedConnection)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in crystallizedProps)
        {
            Assert.True(
                workflowConnProps.Contains(prop.Name.ToLowerInvariant()),
                $"CrystallizedConnection.{prop.Name} has no matching property in WorkflowConnection. " +
                $"Available: {string.Join(", ", workflowConnProps.Order())}");
        }
    }
}
