using AgentCraftLab.Data;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgentCraftLab.Api.Endpoints;

public static class SkillEndpoints
{
    public static void MapSkillEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/skills");

        group.MapGet("/", async (ISkillStore store, [FromServices] SkillRegistryService registry, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var custom = await store.ListAsync(userId);
            var builtin = registry.GetAvailableSkills()
                .Select(s => new { s.Id, Name = s.DisplayName, s.Description, s.Instructions, Category = s.Category.ToString(), s.Icon, Tools = s.Tools ?? [], isBuiltin = true })
                .ToList();
            return Results.Ok(new { builtin, custom });
        });

        group.MapPost("/", async (CreateSkillRequest req, ISkillStore store, IUserContext userCtx) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new ApiError("SKILL_NAME_REQUIRED"));
            var userId = await userCtx.GetUserIdAsync();
            var doc = await store.SaveAsync(userId, req.Name, req.Description ?? "", req.Category ?? "", req.Icon ?? "", req.Instructions ?? "", req.Tools ?? []);
            return Results.Created($"/api/skills/{doc.Id}", doc);
        });

        group.MapPut("/{id}", async (string id, CreateSkillRequest req, ISkillStore store, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var doc = await store.UpdateAsync(userId, id, req.Name ?? "", req.Description ?? "", req.Category ?? "", req.Icon ?? "", req.Instructions ?? "", req.Tools ?? []);
            return doc is not null ? Results.Ok(doc) : Results.NotFound(new ApiError("SKILL_NOT_FOUND", Params: new() { ["id"] = id }));
        });

        group.MapDelete("/{id}", async (string id, ISkillStore store, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var ok = await store.DeleteAsync(userId, id);
            return ok ? Results.NoContent() : Results.NotFound(new ApiError("SKILL_NOT_FOUND", Params: new() { ["id"] = id }));
        });

        // ─── SKILL.md 匯出 ───
        group.MapGet("/{id}/export", async (string id, ISkillStore store, [FromServices] SkillRegistryService registry, IUserContext userCtx) =>
        {
            // 先查 built-in
            var builtin = registry.GetById(id);
            if (builtin is not null)
            {
                var md = SkillMdConverter.ToSkillMd(builtin);
                return Results.Text(md, "text/markdown");
            }

            // 再查 custom
            var doc = await store.GetAsync(id);
            if (doc is null)
                return Results.NotFound(new ApiError("SKILL_NOT_FOUND", Params: new() { ["id"] = id }));

            var skill = SkillRegistryService.ToDefinition(doc);
            var markdown = SkillMdConverter.ToSkillMd(skill);
            return Results.Text(markdown, "text/markdown");
        });

        // ─── SKILL.md 匯入 ───
        group.MapPost("/import", async (HttpContext ctx, ISkillStore store, IUserContext userCtx) =>
        {
            string content;

            if (ctx.Request.HasFormContentType)
            {
                var form = await ctx.Request.ReadFormAsync();
                var file = form.Files.FirstOrDefault();
                if (file is null || file.Length == 0)
                    return Results.BadRequest(new ApiError("SKILL_IMPORT_NO_FILE"));
                using var reader = new StreamReader(file.OpenReadStream());
                content = await reader.ReadToEndAsync();
            }
            else
            {
                using var reader = new StreamReader(ctx.Request.Body);
                content = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(content))
                return Results.BadRequest(new ApiError("SKILL_IMPORT_EMPTY"));

            var skill = SkillMdConverter.FromSkillMd(content);
            if (skill is null)
                return Results.BadRequest(new ApiError("SKILL_IMPORT_INVALID_FORMAT"));

            var userId = await userCtx.GetUserIdAsync();
            var doc = await store.SaveAsync(
                userId,
                skill.DisplayName,
                skill.Description,
                skill.Category.ToString(),
                skill.Icon,
                skill.Instructions,
                skill.Tools ?? []);

            return Results.Created($"/api/skills/{doc.Id}", doc);
        });
    }
}

public record CreateSkillRequest(string? Name, string? Description, string? Category, string? Icon, string? Instructions, List<string>? Tools);
