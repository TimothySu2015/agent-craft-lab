using System.Text.Json;
using System.Text.RegularExpressions;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Strategies;
using AgentCraftLab.Script;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Api.Endpoints;

public static class ScriptGeneratorEndpoints
{
    private const string SystemPrompt = """
        You are a JavaScript code generator for a sandboxed environment (Jint engine).

        Rules:
        - The variable `input` contains the previous node's text output (string).
        - Set the variable `result` to your output value.
        - You may use JSON.parse() if input is JSON.
        - NO require(), NO import, NO fetch, NO setTimeout — sandbox has none of these.
        - NO comments in code — output clean, minimal code only.
        - Output ONLY a JSON object: {"code": "...your code..."}
        - Do NOT wrap code in markdown fences.

        Examples:
        - "Convert JSON array to CSV" →
          {"code": "const rows = JSON.parse(input);\nconst headers = Object.keys(rows[0]);\nconst csv = [headers.join(',')];\nfor (const row of rows) { csv.push(headers.map(h => row[h]).join(',')); }\nresult = csv.join('\\n');"}
        - "Extract all emails" →
          {"code": "const matches = input.match(/[\\w.-]+@[\\w.-]+\\.[a-z]{2,}/gi);\nresult = matches ? matches.join('\\n') : 'No emails found';"}
        - "Count words" →
          {"code": "result = String(input.trim().split(/\\s+/).length);"}
        """;

    public static void MapScriptGeneratorEndpoints(this WebApplication app)
    {
        app.MapPost("/api/script-generator", async (HttpContext ctx,
            ILlmClientFactory clientFactory,
            CancellationToken ct) =>
        {
            var request = await JsonSerializer.DeserializeAsync<ScriptGenRequest>(
                ctx.Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                ct);

            if (request is null || string.IsNullOrWhiteSpace(request.Prompt))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new ApiError("SCRIPT_GEN_PROMPT_REQUIRED"), ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(request.ApiKey))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new ApiError("SCRIPT_GEN_KEY_REQUIRED"), ct);
                return;
            }

            var provider = AgentContextBuilder.NormalizeProvider(request.Provider ?? "openai");
            var credentials = new Dictionary<string, ProviderCredential>
            {
                [provider] = new() { ApiKey = request.ApiKey, Endpoint = request.Endpoint ?? "" }
            };

            var (client, error) = clientFactory.CreateClient(credentials, provider, request.Model ?? "gpt-4o-mini");
            if (client is null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new ApiError("SCRIPT_GEN_CLIENT_ERROR", error), ct);
                return;
            }

            try
            {
                var messages = new List<ChatMessage>
                {
                    new(ChatRole.System, SystemPrompt),
                    new(ChatRole.User, request.Prompt),
                };

                var response = await client.GetResponseAsync(messages, new ChatOptions { Temperature = 0f }, ct);
                var responseText = response.Text ?? "";

                // 從回應中提取 JSON
                var jsonMatch = Regex.Match(responseText, @"\{[\s\S]*""code""[\s\S]*\}", RegexOptions.None, TimeSpan.FromSeconds(2));
                if (!jsonMatch.Success)
                {
                    // fallback：整段回應當作 code
                    await ctx.Response.WriteAsJsonAsync(new { code = responseText.Trim() }, ct);
                    return;
                }

                var json = JsonSerializer.Deserialize<JsonElement>(jsonMatch.Value);
                var code = json.TryGetProperty("code", out var codeElem) ? codeElem.GetString() ?? "" : "";

                await ctx.Response.WriteAsJsonAsync(new { code }, ct);
            }
            catch (Exception ex)
            {
                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsJsonAsync(new ApiError("SCRIPT_GEN_ERROR", ex.Message), ct);
            }
        });

        // Script Test Run — 在沙箱中測試 JS 腳本
        app.MapPost("/api/script-test", async (HttpContext ctx,
            IScriptEngine scriptEngine,
            CancellationToken ct) =>
        {
            var request = await JsonSerializer.DeserializeAsync<ScriptTestRequest>(
                ctx.Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                ct);

            if (request is null || string.IsNullOrWhiteSpace(request.Code))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new ApiError("SCRIPT_TEST_CODE_REQUIRED"), ct);
                return;
            }

            var result = await scriptEngine.ExecuteAsync(request.Code, request.Input ?? "", cancellationToken: ct);

            await ctx.Response.WriteAsJsonAsync(new
            {
                success = result.Success,
                output = result.Output,
                error = result.Error,
                consoleOutput = result.ConsoleOutput,
                elapsedMs = result.Elapsed.TotalMilliseconds,
            }, ct);
        });
    }
}

file record ScriptGenRequest(string? Prompt, string? Provider, string? Model, string? ApiKey, string? Endpoint);
file record ScriptTestRequest(string? Code, string? Input);
