using AgentCraftLab.Data;
using AgentCraftLab.Data.PostgreSQL;
using AgentCraftLab.Engine.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Tests.PostgreSQL;

public class PostgreSqlProviderRegistrationTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddAgentCraftEngine();
        services.AddPostgreSqlDataProvider("Host=localhost;Database=test");
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(typeof(IWorkflowStore), typeof(PgWorkflowStore))]
    [InlineData(typeof(ICredentialStore), typeof(PgCredentialStore))]
    [InlineData(typeof(ISkillStore), typeof(PgSkillStore))]
    [InlineData(typeof(IRequestLogStore), typeof(PgRequestLogStore))]
    [InlineData(typeof(ITemplateStore), typeof(PgTemplateStore))]
    [InlineData(typeof(IKnowledgeBaseStore), typeof(PgKnowledgeBaseStore))]
    [InlineData(typeof(IApiKeyStore), typeof(PgApiKeyStore))]
    [InlineData(typeof(IScheduleStore), typeof(PgScheduleStore))]
    [InlineData(typeof(IExecutionMemoryStore), typeof(PgExecutionMemoryStore))]
    [InlineData(typeof(IEntityMemoryStore), typeof(PgEntityMemoryStore))]
    [InlineData(typeof(IContextualMemoryStore), typeof(PgContextualMemoryStore))]
    [InlineData(typeof(ICheckpointStore), typeof(PgCheckpointStore))]
    [InlineData(typeof(ICraftMdStore), typeof(PgCraftMdStore))]
    [InlineData(typeof(IDataSourceStore), typeof(PgDataSourceStore))]
    [InlineData(typeof(IRefineryStore), typeof(PgRefineryStore))]
    public void AddPostgreSqlDataProvider_Registers_All_15_Stores(Type interfaceType, Type expectedType)
    {
        using var sp = BuildProvider();
        var store = sp.GetRequiredService(interfaceType);
        Assert.IsType(expectedType, store);
    }

    [Fact]
    public void AddPostgreSqlDataProvider_Registers_AppDbContext()
    {
        using var sp = BuildProvider();
        var ctx = sp.GetRequiredService<AppDbContext>();
        Assert.NotNull(ctx);
    }

    [Fact]
    public void AddPostgreSqlDataProvider_Registers_CredentialProtector()
    {
        using var sp = BuildProvider();
        var protector = sp.GetRequiredService<CredentialProtector>();
        Assert.NotNull(protector);
    }
}
