/**
 * Export 部署包 — 產生完整可部署的 .NET Web API 專案。
 * 對齊 Blazor 版 studio-export.js 的功能。
 * 包含：Program.cs（/chat + /chat/stream + /chat/upload + health + A2A/MCP 端點）
 *       .csproj、appsettings.json、Dockerfile、README.md、workflow.json
 */
import type { Node, Edge } from '@xyflow/react'
import type { NodeData } from '@/types/workflow'
import { toWorkflowPayloadJson } from './workflow-payload'
import { useWorkflowStore } from '@/stores/workflow-store'
import { useCredentialStore } from '@/stores/credential-store'

export async function exportDeployPackage(
  name: string, nodes: Node<NodeData>[], edges: Edge[],
  mode: 'project' | 'teams' | 'console' = 'project',
) {
  const safeName = name.replace(/[^a-zA-Z0-9]/g, '') || 'MyWorkflow'
  const settings = useWorkflowStore.getState().workflowSettings
  const workflowJson = toWorkflowPayloadJson(nodes, edges, settings)
  const creds = useCredentialStore.getState().credentials
  const providers = [...new Set(
    nodes.filter((n) => n.type === 'agent' || n.type === 'autonomous')
      .map((n) => (n.data as any).provider).filter(Boolean)
  )]
  if (providers.length === 0) providers.push('openai')

  const files: Record<string, string> = {
    'Program.cs': mode === 'console' ? genConsoleProgramCs() : genProgramCs(),
    [`${safeName}.csproj`]: genCsproj(safeName, mode),
    'workflow.json': workflowJson,
    'appsettings.json': genAppsettings(providers, creds),
    'Dockerfile': genDockerfile(safeName),
    'README.md': genReadme(safeName, mode),
  }

  try {
    const JSZip = (await import('jszip')).default
    const zip = new JSZip()
    for (const [filename, content] of Object.entries(files)) zip.file(filename, content)
    const blob = await zip.generateAsync({ type: 'blob' })
    downloadBlob(blob, `${safeName}.zip`)
  } catch {
    for (const [filename, content] of Object.entries(files)) {
      downloadBlob(new Blob([content], { type: 'text/plain' }), filename)
    }
  }
}

function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url; a.download = filename
  document.body.appendChild(a); a.click()
  document.body.removeChild(a); URL.revokeObjectURL(url)
}

// ─── Program.cs ───
function genProgramCs(): string {
  return `using System.Text.Json;
using AgentCraftLab.Engine.Extensions;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAgentCraftEngine();

var app = builder.Build();
await app.Services.InitializeDatabaseAsync();

var workflowJson = File.ReadAllText("workflow.json");
var config = app.Configuration.GetSection("AgentCraft");

// Load credentials from appsettings.json
Dictionary<string, ProviderCredential> LoadCredentials(IConfiguration section)
{
    var creds = new Dictionary<string, ProviderCredential>();
    foreach (var child in section.GetSection("Credentials").GetChildren())
    {
        creds[child.Key] = new ProviderCredential
        {
            ApiKey = child["ApiKey"] ?? "",
            Endpoint = child["Endpoint"] ?? "",
            Model = child["Model"] ?? ""
        };
    }
    return creds;
}

var defaultCredentials = LoadCredentials(config);

// POST /chat - Full response
app.MapPost("/chat", async (ChatApiRequest req, WorkflowExecutionService engine) =>
{
    var credentials = req.Credentials is { Count: > 0 } ? req.Credentials : defaultCredentials;
    var request = new WorkflowExecutionRequest
    {
        WorkflowJson = workflowJson,
        UserMessage = req.Message,
        Credentials = credentials,
        History = req.History ?? []
    };

    var events = new List<ExecutionEvent>();
    await foreach (var evt in engine.ExecuteAsync(request))
        events.Add(evt);

    var response = string.Join("", events
        .Where(e => e.Type == EventTypes.AgentCompleted && !string.IsNullOrEmpty(e.Text))
        .Select(e => e.Text));

    return Results.Ok(new { response, events });
});

// POST /chat/stream - Server-Sent Events
app.MapPost("/chat/stream", async (ChatApiRequest req, WorkflowExecutionService engine, HttpContext http) =>
{
    http.Response.ContentType = "text/event-stream";
    var credentials = req.Credentials is { Count: > 0 } ? req.Credentials : defaultCredentials;
    var request = new WorkflowExecutionRequest
    {
        WorkflowJson = workflowJson,
        UserMessage = req.Message,
        Credentials = credentials,
        History = req.History ?? []
    };

    await foreach (var evt in engine.ExecuteAsync(request, http.RequestAborted))
    {
        var json = JsonSerializer.Serialize(evt);
        await http.Response.WriteAsync($"data: {json}\\n\\n", http.RequestAborted);
        await http.Response.Body.FlushAsync(http.RequestAborted);
    }
});

// POST /chat/upload - Multipart file upload
app.MapPost("/chat/upload", async (HttpContext http, WorkflowExecutionService engine) =>
{
    var form = await http.Request.ReadFormAsync();
    var message = form["message"].ToString();
    if (string.IsNullOrWhiteSpace(message))
        return Results.BadRequest(new { error = "message is required" });

    FileAttachment? attachment = null;
    var file = form.Files.GetFile("file");
    if (file is { Length: > 0 })
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        attachment = new FileAttachment
        {
            FileName = file.FileName,
            MimeType = file.ContentType,
            Data = ms.ToArray()
        };
    }

    var request = new WorkflowExecutionRequest
    {
        WorkflowJson = workflowJson,
        UserMessage = message,
        Credentials = defaultCredentials,
        Attachment = attachment
    };

    var events = new List<ExecutionEvent>();
    await foreach (var evt in engine.ExecuteAsync(request))
        events.Add(evt);

    var response = string.Join("", events
        .Where(e => e.Type == EventTypes.AgentCompleted && !string.IsNullOrEmpty(e.Text))
        .Select(e => e.Text));

    return Results.Ok(new { response, events });
});

// A2A / MCP / API 端點
app.MapA2AEndpoints();
app.MapMcpEndpoints();
app.MapApiEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

record ChatApiRequest(
    string Message,
    List<ChatHistoryEntry>? History = null,
    Dictionary<string, ProviderCredential>? Credentials = null);
`
}

// ─── Console Program.cs ───
function genConsoleProgramCs(): string {
  return `using System.Text.Json;
using AgentCraftLab.Engine.Extensions;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAgentCraftEngine();
var app = builder.Build();
await app.Services.InitializeDatabaseAsync();

var workflowJson = File.ReadAllText("workflow.json");
var config = app.Configuration.GetSection("AgentCraft");
var credentials = new Dictionary<string, ProviderCredential>();
foreach (var child in config.GetSection("Credentials").GetChildren())
{
    credentials[child.Key] = new ProviderCredential
    {
        ApiKey = child["ApiKey"] ?? "",
        Endpoint = child["Endpoint"] ?? "",
        Model = child["Model"] ?? ""
    };
}

var engine = app.Services.GetRequiredService<WorkflowExecutionService>();

Console.WriteLine("AgentCraftLab Console — Type 'exit' to quit.");
while (true)
{
    Console.Write("\\n> ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input) || input == "exit") break;

    var request = new WorkflowExecutionRequest
    {
        WorkflowJson = workflowJson,
        UserMessage = input,
        Credentials = credentials,
    };

    await foreach (var evt in engine.ExecuteAsync(request))
    {
        if (evt.Type == EventTypes.TextChunk)
            Console.Write(evt.Text);
        else if (evt.Type == EventTypes.AgentCompleted)
            Console.WriteLine();
    }
}
`
}

// ─── .csproj ───
function genCsproj(name: string, mode: string = 'project'): string {
  const sdk = mode === 'console' ? 'Microsoft.NET.Sdk' : 'Microsoft.NET.Sdk.Web'
  const outputType = mode === 'console' ? '\n    <OutputType>Exe</OutputType>' : ''
  return `<Project Sdk="${sdk}">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>${outputType}
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AgentCraftLab.Engine" Version="*-*" />
  </ItemGroup>
  <ItemGroup>
    <None Update="workflow.json" CopyToOutputDirectory="PreserveNewest" />
    <None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
`
}

// ─── appsettings.json ───
function genAppsettings(providers: string[], creds: Record<string, any>): string {
  const credSection: Record<string, any> = {}
  for (const p of providers) {
    credSection[p] = {
      ApiKey: creds[p]?.apiKey ? '(set your key here)' : '',
      Endpoint: creds[p]?.endpoint || '',
      Model: creds[p]?.model || '',
    }
  }
  const config = {
    AgentCraft: { Credentials: credSection },
    Logging: { LogLevel: { Default: 'Information' } },
  }
  return JSON.stringify(config, null, 2)
}

// ─── Dockerfile ───
function genDockerfile(name: string): string {
  return `FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "${name}.dll"]
`
}

// ─── README.md ───
function genReadme(name: string, mode: string = 'project'): string {
  if (mode === 'console') {
    return `# ${name} (Console)

Generated by [AgentCraftLab](https://github.com/anthropics/AgentCraftLab).

## Run

\`\`\`bash
# Configure API keys in appsettings.json, then:
dotnet run
\`\`\`

Interactive console — type messages, get AI responses. Type \`exit\` to quit.
`
  }
  return `# ${name}

Generated by [AgentCraftLab](https://github.com/anthropics/AgentCraftLab).

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| POST | /chat | Full response (JSON) |
| POST | /chat/stream | SSE streaming |
| POST | /chat/upload | Multipart file upload |
| GET | /health | Health check |
| POST | /a2a/{id} | A2A Agent endpoint |
| POST | /mcp/{id} | MCP tool endpoint |

## Run

\`\`\`bash
# Configure API keys in appsettings.json, then:
dotnet run
\`\`\`

## Docker

\`\`\`bash
docker build -t ${name.toLowerCase()} .
docker run -p 8080:8080 ${name.toLowerCase()}
\`\`\`

## Test

\`\`\`bash
curl -X POST http://localhost:5000/chat \\
  -H "Content-Type: application/json" \\
  -d '{"message": "Hello"}'
\`\`\`
`
}
