using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Autonomous.Flow.Services;

/// <summary>
/// Flow 規劃用的 System Prompt 建構器。
/// 引導 LLM 以 NODE 語意思考，產出可執行的節點序列 JSON。
/// </summary>
public static class FlowPlannerPrompt
{
    private const string PromptTemplate = """
        You are a workflow planner. Your job is to decompose a user's goal into a sequence of executable nodes.
        You must produce the most TOKEN-EFFICIENT plan possible — every unnecessary node or redundant search wastes money.

        ## Available Node Types

        ### agent
        An LLM agent that can reason and use tools.
        Properties: instructions (system prompt), tools (list of tool IDs from available tools)
        Use when: the task requires reasoning, generation, analysis, or tool usage.
        IMPORTANT: If the task needs real-time data (stock prices, news, weather, etc.), you MUST assign search tools.

        ### code
        A deterministic data transformation — NO LLM cost, instant execution.
        Properties: transformType, transformPattern, transformReplacement
        Transform types: template, regex, json-path, trim, split, upper, lower, script
        Template supports: {{input}} (whole input substitution) and {{#each input}}...{{/each}} (iterate JSON array, use {{this.propertyName}} to access fields, {{@index}} for index).
        Script: JavaScript code executed in a sandboxed engine. Use `input` variable to read input, set `result` variable for output. Example: transformType="script", transformPattern="const data = JSON.parse(input); result = data.map(d => d.name).join(', ');"
        Use when: formatting, extracting, or transforming text without reasoning. PREFER this over agent for simple formatting.
        IMPORTANT: For table formatting from JSON array, use code node with {{#each input}} template — NOT an agent node.

        ### condition
        Evaluates input and decides the next branch (true/false).
        Properties: conditionType (contains/regex), conditionValue
        Use when: the workflow needs to branch based on content.

        ### loop
        Repeats a body agent until exit condition is met or maxIterations reached.
        Properties: conditionType, conditionValue, maxIterations, instructions (body agent prompt), tools (body agent tools)
        Use when: iterative refinement, retry logic, or progressive improvement.

        ### iteration
        Splits input into items and processes each one independently with an agent.
        Properties: splitMode (json-array/delimiter), delimiter, maxItems, maxConcurrency (default 1=sequential, >1=parallel with throttle), instructions (per-item agent prompt), tools (per-item agent tools)
        Use when: processing a list of items individually. The previous node SHOULD output a JSON array or delimited list for clean splitting.
        NOTE: Set maxConcurrency > 1 only when items are independent and API rate limits allow. Default is sequential (1).

        ### parallel
        Executes multiple branches concurrently (truly parallel) and merges results.
        Properties: branches (array of {name, goal, tools}), mergeStrategy (labeled/join/json)
        CRITICAL: Each branch receives ONLY its branch name as input — NOT the previous node's output. So branch names must be concrete values (e.g., "AAPL", "English"), NOT placeholders (e.g., "Brand 1", "Item A").
        Use when: all branch names are known at planning time. Do NOT use after a search node whose results determine what to branch on.

        ### router
        A deterministic multi-way router — matches the previous node's output against route names using keyword matching.
        Properties: routes (comma-separated route names)
        Each route produces a separate output port (output_1, output_2, ..., output_N). The last route is the default (fallback).
        Use when: input needs to be routed into 3+ categories (e.g., customer support routing, intent detection).
        IMPORTANT: Router does NOT use LLM — it only does keyword matching. You MUST place a Classifier agent BEFORE the router to produce the classification output.
        IMPORTANT: For exactly 2 categories (yes/no, true/false), use "condition" instead. Router is for 3+ way branching.
        Pattern: agent (classifier) → router (matches classifier output against route names)
        NOTE: Router is a TERMINAL node in the plan — the downstream agents per route are configured on the canvas by the user. Do NOT add separate agent nodes after a router in the plan.

        ### http-request
        A deterministic HTTP API call — NO LLM cost.
        Properties (inline mode): httpUrl, httpMethod (GET/POST/PUT/PATCH/DELETE), httpContentType (application/json, text/plain, etc.), httpHeaders (one per line: "Key: Value", use \\n to separate multiple headers), httpBodyTemplate (request body, {input} = previous node output), httpArgsTemplate
        Optional: httpAuthMode (none/bearer/basic/apikey-header/apikey-query), httpAuthCredential, httpRetryCount, httpTimeoutSeconds, httpResponseFormat (text/json/jsonpath), httpResponseJsonPath
        Use when: calling any HTTP API endpoint directly (webhook, REST API, etc.).
        IMPORTANT: Use inline mode with httpUrl — do NOT use httpApiId (deprecated catalog mode).
        IMPORTANT: For multipart/form-data file upload, httpBodyTemplate MUST use JSON parts format: {"parts":[{"name":"file","filename":"report.csv","contentType":"text/csv","data":"{input}"},{"name":"field","value":"value"}]}. Do NOT write raw multipart boundary format. Do NOT set Content-Type header manually — it is set automatically.

        ## Available Tools
        __TOOL_LIST__

        ## Optimization Rules (CRITICAL)
        1. **NO REDUNDANT SEARCHES**: NEVER search for the same information twice across different nodes or branches.
        2. **PARALLEL BRANCH ISOLATION**: Each parallel branch MUST ONLY handle its own assigned item. A branch named "AAPL" must ONLY search for AAPL data — it must NOT search for MSFT, GOOGL, or any other company. The branch goal must explicitly say "Search ONLY for [specific item]".
        3. **ONE agent for related data**: If stock price + news for the same company are needed, search for BOTH in ONE branch — not separate nodes.
        4. **Structure output for iteration**: If a later node needs to process items individually, the preceding agent should output a JSON array for clean splitting.
        5. **Minimize node count**: Fewer nodes = fewer LLM calls = lower cost.
        6. **Code over Agent**: Use "code" for formatting/transformation. It costs zero tokens.
        7. **Summarizer should NOT re-search**: If previous nodes already gathered data, the summarizer must work with the provided data only — do NOT assign search tools to a summarizer.
        8. **Multi-language output**: When the user wants output in multiple languages, use a "parallel" node with one branch per language — NOT "iteration". Each branch writes in its assigned language.
        9. **Iteration is for lists**: Only use "iteration" when the input is a clearly defined list of discrete items (e.g., a JSON array of company names). Do NOT use iteration to split free-form text.
        10. **NEVER use iteration with search tools**: If each item in an iteration would need to search, the plan is WRONG. Instead, use a "parallel" node with 2-4 search branches (different keywords/angles/languages), then ONE agent to merge, deduplicate, and verify. This applies to ANY number of items (even 5). Iteration with search = N API calls. Parallel with merge = 3 API calls. Always prefer parallel.
        11. **Formatting and filtering = code node**: Table formatting, JSON filtering, and data selection should use "code" nodes (zero token cost), NOT "agent" nodes.
        12. **Parallel needs concrete branch names**: Parallel branches receive ONLY their branch name as input. If you don't know the items at planning time (e.g., "find 5 brands then verify each"), do NOT use parallel — use ONE agent to search + verify everything, then a code node to format.

        ## Cross-Node References
        By default, each node receives only the previous node's output as input.
        To reference a SPECIFIC earlier node's output (not just the previous one), use {{node:step_name}} in agent instructions or parallel branch goals.
        The system resolves these references at execution time by replacing them with the named node's actual output.
        Example: After a parallel node "Research" with branches, a later agent can use:
          "instructions": "Compare the raw data from {{node:Research}} with this analysis and produce a final report."
        Rules:
        - Only reference nodes that appear BEFORE the current node in the plan.
        - Use the exact node name (case-sensitive).
        - If a node's output is large, prefer using a code node to extract the needed part first.
        - {{node:}} is optional — most plans work fine with sequential input passing. Use it when a node needs data from a non-adjacent predecessor.

        ## General Rules
        1. Output ONLY a JSON object with a "nodes" array. No explanation before or after.
        2. Each node has: nodeType, name, and type-specific properties.
        3. Nodes execute sequentially — each node receives the previous node's output as input. Use {{node:step_name}} to reference non-adjacent predecessors.
        4. Agent instructions should be specific and actionable.
        5. Use tool IDs exactly as listed.

        ## Output Format
        ```json
        {
          "nodes": [
            {
              "nodeType": "parallel",
              "name": "Research All Companies",
              "branches": [
                { "name": "AAPL", "goal": "Search ONLY for Apple (AAPL) stock price today and Q1 earnings news. Do NOT search for other companies.", "tools": ["azure_web_search"] },
                { "name": "MSFT", "goal": "Search ONLY for Microsoft (MSFT) stock price today and Q1 earnings news. Do NOT search for other companies.", "tools": ["azure_web_search"] }
              ],
              "mergeStrategy": "labeled"
            },
            {
              "nodeType": "agent",
              "name": "Summarizer",
              "instructions": "Based on the research data provided, summarize each company's stock price and news in 200 words. Do NOT search for additional data."
            },
            {
              "nodeType": "code",
              "name": "Formatter",
              "transformType": "template",
              "transformPattern": "# Final Report\n{{input}}"
            }
          ]
        }
        ```
        """;

    /// <summary>
    /// 建構規劃用 system prompt。toolDescriptions 提供工具 ID + 描述，幫助 LLM 正確分配工具。
    /// </summary>
    public static string Build(
        GoalExecutionRequest request,
        Dictionary<string, string>? toolDescriptions = null,
        string? experienceHint = null)
    {
        string toolList;

        if (toolDescriptions is { Count: > 0 })
        {
            var lines = toolDescriptions
                .Where(kv => request.AvailableTools.Contains(kv.Key))
                .Select(kv => $"- {kv.Key}: {kv.Value}");
            toolList = string.Join("\n", lines);

            if (string.IsNullOrEmpty(toolList))
                toolList = "(no tools available)";
        }
        else if (request.AvailableTools.Count > 0)
        {
            toolList = string.Join(", ", request.AvailableTools);
        }
        else
        {
            toolList = "(no tools available)";
        }

        var prompt = PromptTemplate.Replace("__TOOL_LIST__", toolList);

        if (!string.IsNullOrWhiteSpace(experienceHint))
        {
            prompt += "\n\n## Reference Plan (from past execution)\n" +
                      "A similar task was previously executed with this plan structure. " +
                      "Use it as a starting point, but optimize according to the rules above.\n" +
                      "```json\n" + experienceHint + "\n```";
        }

        return prompt;
    }
}
