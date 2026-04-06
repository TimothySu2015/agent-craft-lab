using System.Diagnostics;

namespace AgentCraftLab.Engine.Diagnostics;

/// <summary>
/// 平台自訂 ActivitySource — 追蹤節點層級的執行。
/// 用 session.id tag 配對（不依賴 AsyncLocal 的 Activity.Current 傳播）。
/// </summary>
public static class EngineActivitySource
{
    public const string SessionIdTag = "session.id";

    public static readonly ActivitySource Source = new("AgentCraftLab.Engine");

    /// <summary>
    /// 建立節點執行 Activity，帶 session.id tag 用於 Exporter 配對。
    /// </summary>
    public static Activity? StartNodeExecution(
        string nodeType, string nodeName, string? nodeId = null, string? sessionId = null)
    {
        var activity = Source.StartActivity(nodeName, ActivityKind.Internal);
        if (activity is null) return null;

        activity.SetTag("node.type", nodeType);
        activity.SetTag("node.name", nodeName);
        if (nodeId is not null)
            activity.SetTag("node.id", nodeId);
        if (sessionId is not null)
            activity.SetTag(SessionIdTag, sessionId);
        return activity;
    }
}
