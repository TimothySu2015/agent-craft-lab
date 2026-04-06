using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Renderers;

namespace AgentCraftLab.Tests.Cleaner;

public class MarkdownRendererTests
{
    private static SchemaDefinition TestSchema => new()
    {
        Name = "Test Spec",
        Description = "A test specification",
        JsonSchema = "{}",
    };

    private readonly MarkdownRenderer _renderer = new();

    [Fact]
    public async Task Render_DocumentTitle()
    {
        var json = """
        {
            "document": { "title": "My Project Spec", "version": "1.0", "date": "2026-03-29", "sources": ["a.docx", "b.xlsx"] }
        }
        """;

        var md = await _renderer.RenderAsync(json, TestSchema);

        Assert.StartsWith("# My Project Spec", md);
        Assert.Contains("1.0", md);
        Assert.Contains("2026-03-29", md);
        Assert.Contains("a.docx", md);
    }

    [Fact]
    public async Task Render_SimpleObjectSection()
    {
        var json = """
        {
            "project_overview": {
                "name": "Test Project",
                "objective": "Build something",
                "scope": "Everything"
            }
        }
        """;

        var md = await _renderer.RenderAsync(json, TestSchema);

        Assert.Contains("## Project overview", md);
        Assert.Contains("**Name**", md);
        Assert.Contains("Test Project", md);
    }

    [Fact]
    public async Task Render_ArrayOfStrings()
    {
        var json = """
        {
            "open_questions": ["Q1?", "Q2?", "Q3?"]
        }
        """;

        var md = await _renderer.RenderAsync(json, TestSchema);

        Assert.Contains("## Open questions", md);
        Assert.Contains("- Q1?", md);
        Assert.Contains("- Q3?", md);
    }

    [Fact]
    public async Task Render_ArrayOfSimpleObjects_AsTable()
    {
        var json = """
        {
            "glossary": [
                { "term": "SSO", "definition": "Single Sign-On" },
                { "term": "API", "definition": "Application Programming Interface" }
            ]
        }
        """;

        var md = await _renderer.RenderAsync(json, TestSchema);

        Assert.Contains("## Glossary", md);
        Assert.Contains("| Term |", md);
        Assert.Contains("| SSO |", md);
        Assert.Contains("| --- |", md);
    }

    [Fact]
    public async Task Render_ComplexObjectArray_AsSubSections()
    {
        var json = """
        {
            "functional_requirements": [
                {
                    "id": "FR-001",
                    "title": "User Login",
                    "priority": "Must",
                    "description": "Support email login",
                    "acceptance_criteria": ["Can login with email", "Redirect to home"],
                    "source": "meeting.docx"
                }
            ]
        }
        """;

        var md = await _renderer.RenderAsync(json, TestSchema);

        Assert.Contains("FR-001", md);
        Assert.Contains("User Login", md);
        Assert.Contains("Can login with email", md);
    }

    [Fact]
    public async Task Render_NullFieldsSkipped()
    {
        var json = """
        {
            "project_overview": {
                "name": "Test",
                "client": null,
                "objective": "Build"
            }
        }
        """;

        var md = await _renderer.RenderAsync(json, TestSchema);

        Assert.DoesNotContain("Client", md);
        Assert.Contains("**Name**", md);
    }

    [Fact]
    public async Task Render_InvalidJson_FallbackToCodeBlock()
    {
        var md = await _renderer.RenderAsync("not valid json", TestSchema);

        Assert.Contains("```json", md);
        Assert.Contains("not valid json", md);
    }

    [Fact]
    public void Format_IsMarkdown()
    {
        Assert.Equal("markdown", _renderer.Format);
    }
}
