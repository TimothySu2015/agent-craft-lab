using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgentCraftLab.Api.Endpoints;

/// <summary>
/// MCP / A2A / HTTP API 探索與測試端點。
/// </summary>
public static class DiscoveryEndpoints
{
    public static void MapDiscoveryEndpoints(this WebApplication app)
    {
        // ─── MCP ───

        app.MapPost("/api/mcp/discover", async (McpDiscoverRequest req, [FromServices] McpClientService mcp, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Url))
            {
                return Results.BadRequest(new ApiError("MCP_URL_REQUIRED", "MCP server URL is required"));
            }

            try
            {
                var tools = await mcp.GetToolsAsync(req.Url, ct);
                var toolList = tools.Select(t => new { name = t.Name, description = t.Description }).ToList();
                return Results.Ok(new { healthy = true, tools = toolList });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { healthy = false, error = ex.Message, tools = Array.Empty<object>() });
            }
        });

        // ─── A2A ───

        app.MapPost("/api/a2a/discover", async (A2ADiscoverRequest req, [FromServices] A2AClientService a2a, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Url))
            {
                return Results.BadRequest(new ApiError("A2A_URL_REQUIRED", "A2A agent URL is required"));
            }

            try
            {
                var card = await a2a.DiscoverAsync(req.Url, req.Format ?? "auto", ct);
                return Results.Ok(new { healthy = true, agent = card });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { healthy = false, error = ex.Message });
            }
        });

        app.MapPost("/api/a2a/test", async (A2ATestRequest req, [FromServices] A2AClientService a2a) =>
        {
            if (string.IsNullOrWhiteSpace(req.Url) || string.IsNullOrWhiteSpace(req.Message))
            {
                return Results.BadRequest(new ApiError("A2A_TEST_PARAMS_REQUIRED", "URL and message are required"));
            }

            try
            {
                var result = await a2a.SendMessageAsync(req.Url, req.Message, format: req.Format ?? "auto");
                return Results.Ok(new { success = true, response = result });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, error = ex.Message });
            }
        });

        // ─── HTTP API ───

        app.MapPost("/api/http-tools/test", async (HttpApiTestRequest req, [FromServices] IHttpApiTool httpTool) =>
        {
            if (string.IsNullOrWhiteSpace(req.Url))
            {
                return Results.BadRequest(new ApiError("HTTP_URL_REQUIRED", "URL is required"));
            }

            var apiDef = new HttpApiDefinition
            {
                Name = req.Name ?? "test",
                Url = req.Url,
                Method = req.Method ?? "GET",
                Headers = req.Headers ?? "",
                BodyTemplate = req.Body ?? "",
            };

            try
            {
                var result = await httpTool.CallApiAsync(apiDef, req.Input ?? "");
                return Results.Ok(new { success = true, response = result });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, error = ex.Message });
            }
        });
    }
}

public record McpDiscoverRequest(string? Url);
public record A2ADiscoverRequest(string? Url, string? Format);
public record A2ATestRequest(string? Url, string? Message, string? Format);
public record HttpApiTestRequest(string? Name, string? Url, string? Method, string? Headers, string? Body, string? Input);
