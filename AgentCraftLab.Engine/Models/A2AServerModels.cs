using System.Text.Json.Serialization;

namespace AgentCraftLab.Engine.Models;

// ═══════════════════════════════════════════
// JSON-RPC 2.0
// ═══════════════════════════════════════════

public class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("params")]
    public JsonRpcParams? Params { get; set; }
}

public class JsonRpcParams
{
    [JsonPropertyName("message")]
    public A2AMessage? Message { get; set; }
}

public class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; set; }

    public static JsonRpcResponse Success(object? id, object result) =>
        new() { Id = id, Result = result };

    public static JsonRpcResponse Fail(object? id, int code, string message) =>
        new() { Id = id, Error = new JsonRpcError { Code = code, Message = message } };
}

public class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

// ═══════════════════════════════════════════
// A2A Message & Parts
// ═══════════════════════════════════════════

public class A2AMessage
{
    [JsonPropertyName("messageId")]
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N")[..12];

    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("parts")]
    public List<A2APart> Parts { get; set; } = [];

    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    [JsonPropertyName("contextId")]
    public string? ContextId { get; set; }
}

public class A2APart
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "text";

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("file")]
    public A2AFileContent? File { get; set; }

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    public static A2APart TextPart(string text) => new() { Kind = "text", Text = text };
}

public class A2AFileContent
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    [JsonPropertyName("bytes")]
    public string? Bytes { get; set; }

    [JsonPropertyName("uri")]
    public string? Uri { get; set; }
}

// ═══════════════════════════════════════════
// A2A Task
// ═══════════════════════════════════════════

public class A2ATask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("contextId")]
    public string ContextId { get; set; } = "";

    [JsonPropertyName("status")]
    public A2ATaskStatus Status { get; set; } = new();

    [JsonPropertyName("artifacts")]
    public List<A2AArtifact> Artifacts { get; set; } = [];
}

public class A2ATaskStatus
{
    [JsonPropertyName("state")]
    public string State { get; set; } = TaskStates.Submitted;

    [JsonPropertyName("message")]
    public A2AMessage? Message { get; set; }
}

public class A2AArtifact
{
    [JsonPropertyName("artifactId")]
    public string ArtifactId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("parts")]
    public List<A2APart> Parts { get; set; } = [];
}

// ═══════════════════════════════════════════
// SSE Streaming Events
// ═══════════════════════════════════════════

public class TaskStatusUpdateEvent
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "status-update";

    [JsonPropertyName("taskId")]
    public string TaskId { get; set; } = "";

    [JsonPropertyName("status")]
    public A2ATaskStatus Status { get; set; } = new();

    [JsonPropertyName("final")]
    public bool Final { get; set; }
}

public class TaskArtifactUpdateEvent
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "artifact-update";

    [JsonPropertyName("taskId")]
    public string TaskId { get; set; } = "";

    [JsonPropertyName("artifact")]
    public A2AArtifact Artifact { get; set; } = new();

    [JsonPropertyName("append")]
    public bool Append { get; set; }

    [JsonPropertyName("lastChunk")]
    public bool LastChunk { get; set; }
}

// ═══════════════════════════════════════════
// Agent Card（Google A2A 格式）
// ═══════════════════════════════════════════

public class A2AServerAgentCard
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "0.3";

    [JsonPropertyName("capabilities")]
    public A2ACapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("skills")]
    public List<A2ASkill> Skills { get; set; } = [];

    [JsonPropertyName("defaultInputModes")]
    public List<string> DefaultInputModes { get; set; } = ["text/plain"];

    [JsonPropertyName("defaultOutputModes")]
    public List<string> DefaultOutputModes { get; set; } = ["text/plain"];
}

public class A2ACapabilities
{
    [JsonPropertyName("streaming")]
    public bool Streaming { get; set; } = true;

    [JsonPropertyName("pushNotifications")]
    public bool PushNotifications { get; set; }
}

public class A2ASkill
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("examples")]
    public List<string> Examples { get; set; } = [];
}
