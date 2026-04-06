using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;
using AgentCraftLab.Cleaner.SchemaMapper;

namespace AgentCraftLab.Tests.Cleaner;

public class SchemaMapperTests
{
    private static CleanedDocument MakeDoc(string fileName, params (ElementType Type, string Text)[] elements) =>
        new()
        {
            FileName = fileName,
            Elements = elements.Select((e, i) => new DocumentElement
            {
                Type = e.Type,
                Text = e.Text,
                FileName = fileName,
                Index = i,
            }).ToList(),
        };

    private static SchemaDefinition SimpleSchema => new()
    {
        Name = "Test Schema",
        Description = "A simple test schema",
        JsonSchema = """
        {
            "type": "object",
            "properties": {
                "title": { "type": "string" },
                "items": { "type": "array", "items": { "type": "string" } }
            }
        }
        """,
    };

    // ── LlmSchemaMapper ──

    [Fact]
    public async Task MapAsync_CallsLlmAndReturnsResult()
    {
        var mockLlm = new MockLlmProvider("""
        {
            "title": "Test Project",
            "items": ["Item 1", "Item 2"],
            "open_questions": ["What about security?"]
        }
        """);

        var mapper = new LlmSchemaMapper(mockLlm);
        var doc = MakeDoc("test.docx",
            (ElementType.Title, "Project Overview"),
            (ElementType.NarrativeText, "This is a test project."),
            (ElementType.ListItem, "Item 1"),
            (ElementType.ListItem, "Item 2"));

        var result = await mapper.MapAsync([doc], SimpleSchema);

        Assert.Contains("Test Project", result.Json);
        Assert.Single(result.OpenQuestions);
        Assert.Equal("What about security?", result.OpenQuestions[0]);
        Assert.Equal(1, result.SourceCount);
    }

    [Fact]
    public async Task MapAsync_ExtractsJsonFromCodeFence()
    {
        var mockLlm = new MockLlmProvider("""
        ```json
        { "title": "Extracted", "items": [] }
        ```
        """);

        var mapper = new LlmSchemaMapper(mockLlm);
        var result = await mapper.MapAsync(
            [MakeDoc("test.txt", (ElementType.NarrativeText, "Content"))],
            SimpleSchema);

        Assert.Contains("Extracted", result.Json);
    }

    [Fact]
    public async Task MapAsync_DetectsNullFields()
    {
        var mockLlm = new MockLlmProvider("""
        { "title": null, "items": ["ok"] }
        """);

        var mapper = new LlmSchemaMapper(mockLlm);
        var result = await mapper.MapAsync(
            [MakeDoc("test.txt", (ElementType.NarrativeText, "Content"))],
            SimpleSchema);

        Assert.Contains("title", result.MissingFields);
    }

    [Fact]
    public async Task MapAsync_HandlesMultipleDocuments()
    {
        var mockLlm = new MockLlmProvider("""{ "title": "Combined", "items": [] }""");

        var mapper = new LlmSchemaMapper(mockLlm);
        var result = await mapper.MapAsync(
            [
                MakeDoc("meeting.docx", (ElementType.NarrativeText, "Meeting notes")),
                MakeDoc("budget.xlsx", (ElementType.Table, "| Item | Cost |")),
            ],
            SimpleSchema);

        Assert.Equal(2, result.SourceCount);
        // Verify both files mentioned in the prompt
        Assert.Contains("meeting.docx", mockLlm.LastUserPrompt);
        Assert.Contains("budget.xlsx", mockLlm.LastUserPrompt);
    }

    [Fact]
    public async Task MapAsync_IncludesExtractionGuidance()
    {
        var mockLlm = new MockLlmProvider("""{ "title": "Test", "items": [] }""");
        var schema = new SchemaDefinition
        {
            Name = "Test",
            Description = "Test schema",
            JsonSchema = "{}",
            ExtractionGuidance = "Priority should use MoSCoW",
        };

        var mapper = new LlmSchemaMapper(mockLlm);
        await mapper.MapAsync(
            [MakeDoc("test.txt", (ElementType.NarrativeText, "Content"))],
            schema);

        Assert.Contains("MoSCoW", mockLlm.LastSystemPrompt);
    }

    [Fact]
    public async Task MapAsync_IncludesPageNumberReferences()
    {
        var mockLlm = new MockLlmProvider("""{ "title": "Test", "items": [] }""");
        var mapper = new LlmSchemaMapper(mockLlm);

        var doc = new CleanedDocument
        {
            FileName = "slides.pptx",
            Elements =
            [
                new DocumentElement
                {
                    Type = ElementType.Title,
                    Text = "Slide Title",
                    FileName = "slides.pptx",
                    PageNumber = 3,
                    Index = 0,
                },
            ],
        };

        await mapper.MapAsync([doc], SimpleSchema);

        Assert.Contains("p.3", mockLlm.LastUserPrompt);
    }

    // ── FileSchemaTemplateProvider ──

    [Fact]
    public void TemplateProvider_LoadsFromDirectory()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Data", "schema-templates");
        var provider = new FileSchemaTemplateProvider(dir);

        var templates = provider.ListTemplates();

        Assert.NotEmpty(templates);
        Assert.Contains(templates, t => t.Id == "software-requirements");
    }

    [Fact]
    public void TemplateProvider_GetTemplate_ReturnsDefinition()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Data", "schema-templates");
        var provider = new FileSchemaTemplateProvider(dir);

        var schema = provider.GetTemplate("software-requirements");

        Assert.NotNull(schema);
        Assert.Equal("軟體需求規格書", schema!.Name);
        Assert.Contains("functional_requirements", schema.JsonSchema);
        Assert.NotNull(schema.ExtractionGuidance);
    }

    [Fact]
    public void TemplateProvider_GetTemplate_UnknownId_ReturnsNull()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Data", "schema-templates");
        var provider = new FileSchemaTemplateProvider(dir);

        Assert.Null(provider.GetTemplate("nonexistent"));
    }

    [Fact]
    public void TemplateProvider_MissingDirectory_ReturnsEmpty()
    {
        var provider = new FileSchemaTemplateProvider("/nonexistent/path");
        Assert.Empty(provider.ListTemplates());
    }

    // ── Helpers ──

    private sealed class MockLlmProvider(string response) : ILlmProvider
    {
        public string LastSystemPrompt { get; private set; } = "";
        public string LastUserPrompt { get; private set; } = "";

        public Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
        {
            LastSystemPrompt = systemPrompt;
            LastUserPrompt = userPrompt;
            return Task.FromResult(new LlmResponse(response, 100, 50));
        }
    }
}
