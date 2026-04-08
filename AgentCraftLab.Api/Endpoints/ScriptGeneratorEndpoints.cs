using System.Text.Json;
using System.Text.RegularExpressions;
using AgentCraftLab.Data;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Strategies;
using AgentCraftLab.Script;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Api.Endpoints;

public static class ScriptGeneratorEndpoints
{
    private const string JsSystemPrompt = """
        You are a JavaScript code generator for a sandboxed environment (Jint engine).

        Rules:
        - The variable `input` contains the previous node's text output (string).
        - Set the variable `result` to your output value.
        - You may use JSON.parse() if input is JSON.
        - NO require(), NO import, NO fetch, NO setTimeout — sandbox has none of these.
        - NO comments in code — output clean, minimal code only.
        - Output ONLY a JSON object: {"code": "...your code...", "testInput": "...sample input for testing..."}
        - The testInput should be a realistic sample that demonstrates the script working correctly.
        - Do NOT wrap code in markdown fences.

        Examples:
        - "Convert JSON array to CSV" →
          {"code": "const rows = JSON.parse(input);\nconst headers = Object.keys(rows[0]);\nconst csv = [headers.join(',')];\nfor (const row of rows) { csv.push(headers.map(h => row[h]).join(',')); }\nresult = csv.join('\\n');", "testInput": "[{\"Name\":\"Alice\",\"Score\":95},{\"Name\":\"Bob\",\"Score\":87}]"}
        - "Extract all emails" →
          {"code": "const matches = input.match(/[\\w.-]+@[\\w.-]+\\.[a-z]{2,}/gi);\nresult = matches ? matches.join('\\n') : 'No emails found';", "testInput": "Contact us at test@example.com or info@test.org for details."}
        - "Count words" →
          {"code": "result = String(input.trim().split(/\\s+/).length);", "testInput": "Hello world from the sandbox"}
        """;

    private const string CSharpSystemPrompt = """
        You are a C# code generator for a sandboxed environment (Roslyn runtime compilation).

        Rules:
        - The parameter `input` is a string containing the previous node's text output.
        - Use `return` to return your output value. The return type is `object`.
        - You may use JsonSerializer.Deserialize<T>() if input is JSON.
        - Available: System, System.Linq, System.Collections.Generic, System.Text, System.Text.Json, System.Text.RegularExpressions.
        - NO File, NO Directory, NO Process, NO HttpClient, NO reflection — sandbox blocks these.
        - NO comments in code — output clean, minimal code only.
        - Write only the method body (no class, no method signature).
        - Output ONLY a JSON object: {"code": "...your code...", "testInput": "...sample input for testing..."}
        - The testInput should be a realistic sample that demonstrates the script working correctly.
        - Do NOT wrap code in markdown fences.

        Examples:
        - "Convert JSON array to CSV" →
          {"code": "var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(input)!;\nvar headers = rows[0].Keys.ToList();\nvar csv = new List<string> { string.Join(\",\", headers) };\nforeach (var row in rows) { csv.Add(string.Join(\",\", headers.Select(h => row[h].ToString()))); }\nreturn string.Join(\"\\n\", csv);", "testInput": "[{\"Name\":\"Alice\",\"Score\":95},{\"Name\":\"Bob\",\"Score\":87}]"}
        - "Extract all emails" →
          {"code": "var matches = Regex.Matches(input, @\"[\\w.-]+@[\\w.-]+\\.[a-z]{2,}\", RegexOptions.IgnoreCase);\nreturn matches.Count > 0 ? string.Join(\"\\n\", matches.Select(m => m.Value)) : \"No emails found\";", "testInput": "Contact us at test@example.com or info@test.org for details."}
        - "Count words" →
          {"code": "return input.Trim().Split(new[] { ' ', '\\t', '\\n', '\\r' }, StringSplitOptions.RemoveEmptyEntries).Length.ToString();", "testInput": "Hello world from the sandbox"}
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

            // 從後端 CredentialStore 讀取加密的 credentials，fallback 到前端傳入的 apiKey
            var credentials = await AgUiEndpoints.ResolveCredentialsAsync(
                ctx, new Dictionary<string, object>(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var provider = AgentContextBuilder.NormalizeProvider(request.Provider ?? "openai");

            // 如果 CredentialStore 沒有，才用前端傳入的 apiKey
            if (!credentials.ContainsKey(provider) && !string.IsNullOrWhiteSpace(request.ApiKey))
            {
                credentials[provider] = new() { ApiKey = request.ApiKey, Endpoint = request.Endpoint ?? "" };
            }

            if (!credentials.ContainsKey(provider))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new ApiError("SCRIPT_GEN_KEY_REQUIRED"), ct);
                return;
            }

            var (client, error) = clientFactory.CreateClient(credentials, provider, request.Model ?? "gpt-4o-mini");
            if (client is null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new ApiError("SCRIPT_GEN_CLIENT_ERROR", error), ct);
                return;
            }

            try
            {
                var isCSharp = string.Equals(request.Language, "csharp", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(request.Language, "c#", StringComparison.OrdinalIgnoreCase);
                var systemPrompt = isCSharp ? CSharpSystemPrompt : JsSystemPrompt;

                var messages = new List<ChatMessage>
                {
                    new(ChatRole.System, systemPrompt),
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
                var testInput = json.TryGetProperty("testInput", out var testElem) ? testElem.GetString() ?? "" : "";

                await ctx.Response.WriteAsJsonAsync(new { code, testInput }, ct);
            }
            catch (Exception ex)
            {
                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsJsonAsync(new ApiError("SCRIPT_GEN_ERROR", ex.Message), ct);
            }
        });

        // Script Test Run — 在沙箱中測試腳本（JS / C#）
        app.MapPost("/api/script-test", async (HttpContext ctx,
            IScriptEngine scriptEngine,
            CancellationToken ct) =>
        {
            var factory = ctx.RequestServices.GetService<IScriptEngineFactory>();

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

            var engine = factory is not null && !string.IsNullOrWhiteSpace(request.Language)
                ? factory.GetEngine(request.Language)
                : scriptEngine;

            var result = await engine.ExecuteAsync(request.Code, request.Input ?? "", cancellationToken: ct);

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

file record ScriptGenRequest(string? Prompt, string? Provider, string? Model, string? ApiKey, string? Endpoint, string? Language);
file record ScriptTestRequest(string? Code, string? Input, string? Language);
