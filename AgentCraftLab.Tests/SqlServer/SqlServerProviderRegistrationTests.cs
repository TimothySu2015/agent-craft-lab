using AgentCraftLab.Data;
using AgentCraftLab.Data.SqlServer;
using AgentCraftLab.Engine.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Tests.SqlServer;

public class SqlServerProviderRegistrationTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddAgentCraftEngine();
        services.AddSqlServerDataProvider("Server=localhost;Database=test;Trusted_Connection=True");
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(typeof(IWorkflowStore), typeof(SqlWorkflowStore))]
    [InlineData(typeof(ICredentialStore), typeof(SqlCredentialStore))]
    [InlineData(typeof(ISkillStore), typeof(SqlSkillStore))]
    [InlineData(typeof(IRequestLogStore), typeof(SqlRequestLogStore))]
    [InlineData(typeof(ITemplateStore), typeof(SqlTemplateStore))]
    [InlineData(typeof(IKnowledgeBaseStore), typeof(SqlKnowledgeBaseStore))]
    [InlineData(typeof(IApiKeyStore), typeof(SqlApiKeyStore))]
    [InlineData(typeof(IScheduleStore), typeof(SqlScheduleStore))]
    [InlineData(typeof(IExecutionMemoryStore), typeof(SqlExecutionMemoryStore))]
    [InlineData(typeof(IEntityMemoryStore), typeof(SqlEntityMemoryStore))]
    [InlineData(typeof(IContextualMemoryStore), typeof(SqlContextualMemoryStore))]
    [InlineData(typeof(ICheckpointStore), typeof(SqlCheckpointStore))]
    [InlineData(typeof(ICraftMdStore), typeof(SqlCraftMdStore))]
    [InlineData(typeof(IDataSourceStore), typeof(SqlDataSourceStore))]
    [InlineData(typeof(IRefineryStore), typeof(SqlRefineryStore))]
    public void AddSqlServerDataProvider_Registers_All_15_Stores(Type interfaceType, Type expectedType)
    {
        using var sp = BuildProvider();
        var store = sp.GetRequiredService(interfaceType);
        Assert.IsType(expectedType, store);
    }

    [Fact]
    public void AddSqlServerDataProvider_Registers_AppDbContext()
    {
        using var sp = BuildProvider();
        var ctx = sp.GetRequiredService<AppDbContext>();
        Assert.NotNull(ctx);
    }

    [Fact]
    public void AddSqlServerDataProvider_Registers_CredentialProtector()
    {
        using var sp = BuildProvider();
        var protector = sp.GetRequiredService<CredentialProtector>();
        Assert.NotNull(protector);
    }
}
