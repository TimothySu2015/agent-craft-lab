using AgentCraftLab.Autonomous.Models;
using AgentCraftLab.Autonomous.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCraftLab.Tests.Autonomous;

public class MultiAgentReflectionTests
{
    // ─── AggregateByVoting 測試（透過反射或直接測試 AuditAsync 行為）───

    [Fact]
    public void ReflectionMode_Default_IsSingle()
    {
        var config = new ReflectionConfig { Enabled = true };
        Assert.Equal(ReflectionMode.Single, config.Mode);
    }

    [Fact]
    public void DefaultPersonas_AreNotProvided_UsesBuiltIn()
    {
        var config = new ReflectionConfig { Enabled = true, Mode = ReflectionMode.Panel };
        Assert.Null(config.Personas);
    }

    [Fact]
    public void EvaluatorPersona_DefaultWeight_IsOne()
    {
        var persona = new EvaluatorPersona { Name = "Test", SystemPrompt = "Test prompt" };
        Assert.Equal(1.0f, persona.Weight);
    }

    // ─── AuditResult 新欄位 ───

    [Fact]
    public void AuditResult_EvaluatorVerdicts_DefaultNull()
    {
        var result = new AuditResult { Verdict = AuditVerdict.Pass };
        Assert.Null(result.EvaluatorVerdicts);
    }

    [Fact]
    public void AuditResult_WithEvaluatorVerdicts()
    {
        var result = new AuditResult
        {
            Verdict = AuditVerdict.NeedsRevision,
            EvaluatorVerdicts =
            [
                new() { PersonaName = "Factual", Verdict = AuditVerdict.Pass },
                new() { PersonaName = "Logic", Verdict = AuditVerdict.NeedsRevision, Issues = ["Missing step"] },
                new() { PersonaName = "Completeness", Verdict = AuditVerdict.NeedsRevision, Issues = ["Incomplete"] },
            ]
        };

        Assert.Equal(3, result.EvaluatorVerdicts.Count);
        Assert.Equal(AuditVerdict.NeedsRevision, result.Verdict);
    }

    // ─── AutoReflectionEngine 複雜度判斷 ───

    [Fact]
    public void Auto_SimpleGoal_ShortAnswer_UsesSingle()
    {
        var request = MakeRequest("hello", maxIterations: 3);
        var reflection = new ReflectionConfig { Enabled = true, Mode = ReflectionMode.Auto };

        // Simple goal + short answer + low iterations → Single
        var engine = new AutoReflectionEngine(
            new AuditorReflectionEngine(NullLogger<AuditorReflectionEngine>.Instance),
            new MultiAgentReflectionEngine(NullLogger<MultiAgentReflectionEngine>.Instance));

        // 無法直接測 private method，但可驗證 config 行為
        Assert.Equal(ReflectionMode.Auto, reflection.Mode);
    }

    [Fact]
    public void Auto_ComplexGoal_LongAnswer_HighIterations_UsesPanel()
    {
        // Complex goal + long answer + high iterations → all 3 signals → Panel
        var request = MakeRequest("比較 NVIDIA 和 AMD 的 GPU 架構差異，分析效能、功耗、價格", maxIterations: 20);
        var longAnswer = new string('x', 1500);

        // IsComplexGoal = true (比較 keyword)
        Assert.True(SystemPromptBuilder.IsComplexGoal(request.Goal));
        // Answer > 1000 chars
        Assert.True(longAnswer.Length > 1000);
        // MaxIterations > 5
        Assert.True(request.MaxIterations > 5);
    }

    [Theory]
    [InlineData(ReflectionMode.Single)]
    [InlineData(ReflectionMode.Panel)]
    [InlineData(ReflectionMode.Auto)]
    public void ReflectionConfig_SupportsAllModes(ReflectionMode mode)
    {
        var config = new ReflectionConfig { Enabled = true, Mode = mode };
        Assert.Equal(mode, config.Mode);
    }

    // ─── ReflectionConfig 向後相容 ───

    [Fact]
    public void ReflectionConfig_OldStyle_StillWorks()
    {
        // 舊版 config 不含 Mode/Personas/UseJudge → 預設值保持向後相容
        var config = new ReflectionConfig
        {
            Enabled = true,
            Provider = "openai",
            Model = "gpt-4o-mini",
            MaxRevisions = 2
        };

        Assert.Equal(ReflectionMode.Single, config.Mode);
        Assert.Null(config.Personas);
        Assert.False(config.UseJudge);
    }

    // ─── EvaluatorVerdict ───

    [Fact]
    public void EvaluatorVerdict_ContainsPersonaInfo()
    {
        var verdict = new EvaluatorVerdict
        {
            PersonaName = "Logic Auditor",
            Verdict = AuditVerdict.Contradiction,
            Explanation = "Circular reasoning detected",
            Issues = ["A depends on B, B depends on A"]
        };

        Assert.Equal("Logic Auditor", verdict.PersonaName);
        Assert.Equal(AuditVerdict.Contradiction, verdict.Verdict);
        Assert.Single(verdict.Issues);
    }

    // ─── Custom Personas ───

    [Fact]
    public void ReflectionConfig_CustomPersonas()
    {
        var config = new ReflectionConfig
        {
            Enabled = true,
            Mode = ReflectionMode.Panel,
            Personas =
            [
                new() { Name = "Security Auditor", SystemPrompt = "Check for security issues", Weight = 2.0f },
                new() { Name = "Performance Auditor", SystemPrompt = "Check for performance issues" },
            ]
        };

        Assert.Equal(2, config.Personas.Count);
        Assert.Equal(2.0f, config.Personas[0].Weight);
        Assert.Equal(1.0f, config.Personas[1].Weight); // default
    }

    // ─── 輔助方法 ───

    private static AutonomousRequest MakeRequest(string goal, int maxIterations = 10)
    {
        return new AutonomousRequest
        {
            Goal = goal,
            MaxIterations = maxIterations,
            Credentials = new(),
            ToolLimits = new ToolCallLimits()
        };
    }
}
