using AgentCraftLab.Autonomous.Services;
using AgentCraftLab.Engine.Data;

namespace AgentCraftLab.Tests.Autonomous;

public class MemoryFormatTests
{
    // ─── FormatEntityMemory ───

    [Fact]
    public void FormatEntityMemory_WithEntities_ContainsNameAndFacts()
    {
        var entities = new List<EntityMemoryDocument>
        {
            new()
            {
                EntityName = "NVIDIA",
                EntityType = "organization",
                Facts = "[\"GPU manufacturer\",\"Revenue $39B\"]"
            },
            new()
            {
                EntityName = "Tesla",
                EntityType = "organization",
                Facts = "[\"Electric cars\"]"
            }
        };

        var result = ExecutionMemoryService.FormatEntityMemory(entities);

        Assert.Contains("Known Entities", result);
        Assert.Contains("**NVIDIA**", result);
        Assert.Contains("organization", result);
        Assert.Contains("GPU manufacturer", result);
        Assert.Contains("**Tesla**", result);
    }

    [Fact]
    public void FormatEntityMemory_InvalidFactsJson_FallsBackToRawText()
    {
        var entities = new List<EntityMemoryDocument>
        {
            new()
            {
                EntityName = "Test",
                EntityType = "concept",
                Facts = "not valid json"
            }
        };

        var result = ExecutionMemoryService.FormatEntityMemory(entities);

        Assert.Contains("**Test**", result);
        Assert.Contains("not valid json", result);
    }

    [Fact]
    public void FormatEntityMemory_EmptyFacts_ShowsNoFacts()
    {
        var entities = new List<EntityMemoryDocument>
        {
            new()
            {
                EntityName = "Empty",
                EntityType = "concept",
                Facts = "[]"
            }
        };

        var result = ExecutionMemoryService.FormatEntityMemory(entities);

        Assert.Contains("(no facts)", result);
    }

    [Fact]
    public void FormatEntityMemory_LimitsFacts_ToThree()
    {
        var entities = new List<EntityMemoryDocument>
        {
            new()
            {
                EntityName = "Big",
                EntityType = "concept",
                Facts = "[\"fact1\",\"fact2\",\"fact3\",\"fact4\",\"fact5\"]"
            }
        };

        var result = ExecutionMemoryService.FormatEntityMemory(entities);

        Assert.Contains("fact1", result);
        Assert.Contains("fact3", result);
        Assert.DoesNotContain("fact4", result);
    }

    // ─── FormatContextualMemory ───

    [Fact]
    public void FormatContextualMemory_WithPatterns_ContainsTypeAndDescription()
    {
        var patterns = new List<ContextualMemoryDocument>
        {
            new()
            {
                PatternType = "preference",
                Description = "User prefers parallel search",
                Confidence = 0.9f
            },
            new()
            {
                PatternType = "topic_interest",
                Description = "Frequently asks about AI",
                Confidence = 0.7f
            }
        };

        var result = ExecutionMemoryService.FormatContextualMemory(patterns);

        Assert.Contains("User Patterns", result);
        Assert.Contains("[preference]", result);
        Assert.Contains("User prefers parallel search", result);
        Assert.Contains("0.9", result);
        Assert.Contains("[topic_interest]", result);
    }
}
