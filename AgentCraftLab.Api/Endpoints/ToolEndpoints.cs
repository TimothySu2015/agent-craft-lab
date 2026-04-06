using AgentCraftLab.Engine.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgentCraftLab.Api.Endpoints;

public static class ToolEndpoints
{
    public static void MapToolEndpoints(this WebApplication app)
    {
        app.MapGet("/api/tools", ([FromServices] ToolRegistryService registry) =>
        {
            var tools = registry.GetAvailableTools()
                .Select(t => new { t.Id, Name = t.DisplayName, t.Description, Category = t.Category.ToString(), t.Icon })
                .ToList();
            return Results.Ok(tools);
        });
    }
}
