using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;
using AgentCraftLab.Cleaner.SchemaMapper;

namespace AgentCraftLab.Tests.Cleaner;

public class MultiLayerSchemaMapperTests
{
    private static CleanedDocument MakeDoc(string fileName, params string[] texts) =>
        new()
        {
            FileName = fileName,
            Elements = texts.Select((t, i) => new DocumentElement
            {
                Type = ElementType.NarrativeText, Text = t, FileName = fileName, Index = i,
            }).ToList(),
        };

    private static SchemaDefinition TestSchema => new()
    {
        Name = "Test Schema",
        Description = "A test schema",
        JsonSchema = """
        {
            "type": "object",
            "properties": {
                "overview": {
                    "type": "object",
                    "properties": {
                        "name": { "type": "string" },
                        "description": { "type": "string" }
                    }
                },
                "items": {
                    "type": "array",
                    "items": { "type": "object", "properties": { "title": { "type": "string" } } }
                }
            }
        }
        """,
    };

    // ═══════════════════════════════════════
    // ExtractSchemaSections
    // ═══════════════════════════════════════

    [Fact]
    public void ExtractSchemaSections_ParsesTopLevelProperties()
    {
        var sections = MultiLayerSchemaMapper.ExtractSchemaSections(TestSchema.JsonSchema);

        Assert.Equal(2, sections.Count);
        Assert.True(sections.ContainsKey("overview"));
        Assert.True(sections.ContainsKey("items"));
    }

    [Fact]
    public void ExtractSchemaSections_ReturnsSubSchema()
    {
        var sections = MultiLayerSchemaMapper.ExtractSchemaSections(TestSchema.JsonSchema);

        Assert.Contains("name", sections["overview"]);
        Assert.Contains("description", sections["overview"]);
        Assert.Contains("title", sections["items"]);
    }

    [Fact]
    public void ExtractSchemaSections_InvalidSchema_ReturnsEmpty()
    {
        var sections = MultiLayerSchemaMapper.ExtractSchemaSections("not valid json");
        Assert.Empty(sections);
    }

    [Fact]
    public void ExtractSchemaSections_NoProperties_ReturnsEmpty()
    {
        var sections = MultiLayerSchemaMapper.ExtractSchemaSections("""{ "type": "object" }""");
        Assert.Empty(sections);
    }

    // ═══════════════════════════════════════
    // MapAsync — Full Pipeline
    // ═══════════════════════════════════════

    [Fact]
    public async Task MapAsync_PreciseMode_RunsAllLayers()
    {
        var mockLlm = new SequentialMockLlm(
        [
            // Layer 2: PlanSections
            """[{"section": "overview", "hasData": true, "searchQueries": ["name"]}, {"section": "items", "hasData": false, "searchQueries": []}]""",
            // Layer 3: overview 區塊擷取
            """{"name": "Test Project", "description": "A test"}""",
        ]);

        var searchCalled = false;
        SearchCallback search = (query, topK, ct) =>
        {
            searchCalled = true;
            return Task.FromResult<IReadOnlyList<string>>(["relevant chunk about Test Project"]);
        };

        var mapper = new MultiLayerSchemaMapper(mockLlm, search);
        var result = await mapper.MapAsync(
            [MakeDoc("test.txt", "Test Project description")],
            TestSchema);

        Assert.Contains("Test Project", result.Json);
        Assert.True(searchCalled);
        Assert.Equal(1, result.SourceCount);
        Assert.Contains("items", result.MissingFields); // hasData=false → missing
    }

    [Fact]
    public async Task MapAsync_WithChallenge_ReturnsConfidence()
    {
        var mockLlm = new SequentialMockLlm(
        [
            // Layer 2
            """[{"section": "overview", "hasData": true, "searchQueries": ["name"]}]""",
            // Layer 3: overview
            """{"name": "My Project", "description": "desc"}""",
            // Layer 4: Challenge
            """{"challenges": [{"field": "name", "original": "My Project", "reason": "looks correct", "suggested": null, "confidence": 0.9}], "verified": ["description"]}""",
        ]);

        SearchCallback search = (q, k, ct) =>
            Task.FromResult<IReadOnlyList<string>>(["chunk"]);

        var mapper = new MultiLayerSchemaMapper(mockLlm, search);
        var options = new SchemaMapperOptions { EnableChallenge = true };
        var result = await mapper.MapAsync(
            [MakeDoc("test.txt", "My Project")],
            TestSchema, options);

        Assert.NotEmpty(result.Challenges);
        Assert.True(result.OverallConfidence <= 1.0f);
        Assert.True(result.OverallConfidence > 0f);
    }

    [Fact]
    public async Task MapAsync_WithChallenge_ClassifiesActions()
    {
        var mockLlm = new SequentialMockLlm(
        [
            // Layer 2
            """[{"section": "overview", "hasData": true, "searchQueries": ["test"]}]""",
            // Layer 3
            """{"name": "X", "description": "Y"}""",
            // Layer 4: 三種信心度
            "{\"challenges\": [{\"field\": \"name\", \"original\": \"X\", \"reason\": \"ok\", \"suggested\": null, \"confidence\": 0.9}, {\"field\": \"description\", \"original\": \"Y\", \"reason\": \"maybe wrong\", \"suggested\": \"Z\", \"confidence\": 0.6}, {\"field\": \"other\", \"original\": \"A\", \"reason\": \"definitely wrong\", \"suggested\": \"B\", \"confidence\": 0.3}], \"verified\": []}",
        ]);

        SearchCallback search = (q, k, ct) =>
            Task.FromResult<IReadOnlyList<string>>(["chunk"]);

        var mapper = new MultiLayerSchemaMapper(mockLlm, search);
        var result = await mapper.MapAsync(
            [MakeDoc("test.txt", "content")],
            TestSchema, new SchemaMapperOptions { EnableChallenge = true });

        var accept = result.Challenges.Where(c => c.Action == ChallengeAction.Accept).ToList();
        var flag = result.Challenges.Where(c => c.Action == ChallengeAction.Flag).ToList();
        var reject = result.Challenges.Where(c => c.Action == ChallengeAction.Reject).ToList();

        Assert.Single(accept);  // 0.9 → Accept
        Assert.Single(flag);    // 0.6 → Flag
        Assert.Single(reject);  // 0.3 → Reject
    }

    [Fact]
    public async Task MapAsync_NoChallenge_ConfidenceIs1()
    {
        var mockLlm = new SequentialMockLlm(
        [
            """[{"section": "overview", "hasData": true, "searchQueries": ["test"]}]""",
            """{"name": "X"}""",
        ]);

        SearchCallback search = (q, k, ct) =>
            Task.FromResult<IReadOnlyList<string>>(["chunk"]);

        var mapper = new MultiLayerSchemaMapper(mockLlm, search);
        var result = await mapper.MapAsync(
            [MakeDoc("test.txt", "content")],
            TestSchema);

        Assert.Empty(result.Challenges);
        Assert.Equal(1.0f, result.OverallConfidence);
    }

    // ═══════════════════════════════════════
    // Token Tracking
    // ═══════════════════════════════════════

    [Fact]
    public async Task MapAsync_AccumulatesTokens()
    {
        var mockLlm = new SequentialMockLlm(
        [
            """[{"section": "overview", "hasData": true, "searchQueries": ["test"]}]""",
            """{"name": "X"}""",
        ], inputTokens: 100, outputTokens: 50);

        SearchCallback search = (q, k, ct) =>
            Task.FromResult<IReadOnlyList<string>>(["chunk"]);

        var mapper = new MultiLayerSchemaMapper(mockLlm, search);
        var result = await mapper.MapAsync(
            [MakeDoc("test.txt", "content")], TestSchema);

        // 2 LLM calls × (100 in + 50 out)
        Assert.Equal(200, result.TotalInputTokens);
        Assert.Equal(100, result.TotalOutputTokens);
        Assert.Equal(300, result.TotalTokens);
    }

    // ═══════════════════════════════════════
    // Progress Callback
    // ═══════════════════════════════════════

    [Fact]
    public async Task MapAsync_ReportsProgress()
    {
        var mockLlm = new SequentialMockLlm(
        [
            """[{"section": "overview", "hasData": true, "searchQueries": ["test"]}]""",
            """{"name": "X"}""",
        ]);

        SearchCallback search = (q, k, ct) =>
            Task.FromResult<IReadOnlyList<string>>(["chunk"]);

        var progressMessages = new List<string>();
        var mapper = new MultiLayerSchemaMapper(mockLlm, search, msg => progressMessages.Add(msg));
        await mapper.MapAsync([MakeDoc("test.txt", "content")], TestSchema);

        Assert.True(progressMessages.Count >= 4); // Layer 2 plan + found + Layer 3 extracting + completed
        Assert.Contains(progressMessages, m => m.Contains("Layer 2"));
        Assert.Contains(progressMessages, m => m.Contains("Layer 3"));
    }

    // ═══════════════════════════════════════
    // Edge Cases
    // ═══════════════════════════════════════

    [Fact]
    public async Task MapAsync_NoSearchCallback_StillWorks()
    {
        var mockLlm = new SequentialMockLlm(
        [
            """[{"section": "overview", "hasData": true, "searchQueries": ["test"]}]""",
            """{"name": "Fallback"}""",
        ]);

        // No search callback
        var mapper = new MultiLayerSchemaMapper(mockLlm, search: null);
        var result = await mapper.MapAsync(
            [MakeDoc("test.txt", "content")], TestSchema);

        Assert.Contains("Fallback", result.Json);
    }

    [Fact]
    public async Task MapAsync_LlmReturnsArray_HandlesCorrectly()
    {
        var mockLlm = new SequentialMockLlm(
        [
            """[{"section": "items", "hasData": true, "searchQueries": ["items"]}, {"section": "overview", "hasData": false, "searchQueries": []}]""",
            // Layer 3 returns array (not object) for "items"
            """[{"title": "Item 1"}, {"title": "Item 2"}]""",
        ]);

        SearchCallback search = (q, k, ct) =>
            Task.FromResult<IReadOnlyList<string>>(["chunk"]);

        var mapper = new MultiLayerSchemaMapper(mockLlm, search);
        var result = await mapper.MapAsync(
            [MakeDoc("test.txt", "items content")], TestSchema);

        Assert.Contains("Item 1", result.Json);
        Assert.Contains("Item 2", result.Json);
    }

    [Fact]
    public async Task MapAsync_BadLlmResponse_FallbackPlans()
    {
        // Layer 2 returns garbage → fallback to all sections with hasData=true
        var mockLlm = new SequentialMockLlm(
        [
            "this is not json at all",
            """{"name": "Recovered"}""", // overview
            """[{"title": "Recovered Item"}]""", // items
        ]);

        SearchCallback search = (q, k, ct) =>
            Task.FromResult<IReadOnlyList<string>>(["chunk"]);

        var mapper = new MultiLayerSchemaMapper(mockLlm, search);
        var result = await mapper.MapAsync(
            [MakeDoc("test.txt", "content")], TestSchema);

        // Should not crash — fallback plans all sections as hasData=true
        Assert.NotNull(result.Json);
    }

    [Fact]
    public async Task MapAsync_InvalidSchema_FallsBackToSingleLayer()
    {
        var mockLlm = new SequentialMockLlm(
        [
            """{"name": "SingleLayer"}""",
        ]);

        var mapper = new MultiLayerSchemaMapper(mockLlm);

        // Schema with no "properties" → ExtractSchemaSections returns empty → fallback
        var schema = new SchemaDefinition
        {
            Name = "Simple",
            Description = "test",
            JsonSchema = """{ "type": "string" }""",
        };

        var result = await mapper.MapAsync(
            [MakeDoc("test.txt", "content")], schema);

        Assert.Contains("SingleLayer", result.Json);
    }

    // ═══════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════

    private sealed class SequentialMockLlm : ILlmProvider
    {
        private readonly string[] _responses;
        private readonly int _inputTokens;
        private readonly int _outputTokens;
        private int _callIndex;

        public SequentialMockLlm(string[] responses, int inputTokens = 100, int outputTokens = 50)
        {
            _responses = responses;
            _inputTokens = inputTokens;
            _outputTokens = outputTokens;
        }

        public Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
        {
            var idx = Interlocked.Increment(ref _callIndex) - 1;
            var response = idx < _responses.Length ? _responses[idx] : "{}";
            return Task.FromResult(new LlmResponse(response, _inputTokens, _outputTokens));
        }
    }
}
