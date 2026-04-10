using AgentCraftLab.Data;
using AgentCraftLab.Data.Sqlite;
using AgentCraftLab.Engine.Extensions;
using AgentCraftLab.Data.MongoDB;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Tests.MongoDB;

public class MongoDbProviderRegistrationTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddAgentCraftEngine();
        services.AddSqliteDataProvider();
        services.AddMongoDbProvider("mongodb://localhost:27017", "test");
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(typeof(IWorkflowStore), typeof(MongoWorkflowStore))]
    [InlineData(typeof(ICredentialStore), typeof(MongoCredentialStore))]
    [InlineData(typeof(ISkillStore), typeof(MongoSkillStore))]
    [InlineData(typeof(IRequestLogStore), typeof(MongoRequestLogStore))]
    [InlineData(typeof(ITemplateStore), typeof(MongoTemplateStore))]
    [InlineData(typeof(IKnowledgeBaseStore), typeof(MongoKnowledgeBaseStore))]
    [InlineData(typeof(IApiKeyStore), typeof(MongoApiKeyStore))]
    [InlineData(typeof(IScheduleStore), typeof(MongoScheduleStore))]
    [InlineData(typeof(IExecutionMemoryStore), typeof(MongoExecutionMemoryStore))]
    [InlineData(typeof(IEntityMemoryStore), typeof(MongoEntityMemoryStore))]
    [InlineData(typeof(IContextualMemoryStore), typeof(MongoContextualMemoryStore))]
    [InlineData(typeof(ICheckpointStore), typeof(MongoCheckpointStore))]
    [InlineData(typeof(ICraftMdStore), typeof(MongoCraftMdStore))]
    [InlineData(typeof(IDataSourceStore), typeof(MongoDataSourceStore))]
    [InlineData(typeof(IRefineryStore), typeof(MongoRefineryStore))]
    public void AddMongoDbProvider_Replaces_All_15_Stores(Type interfaceType, Type expectedType)
    {
        using var sp = BuildProvider();
        var store = sp.GetRequiredService(interfaceType);
        Assert.IsType(expectedType, store);
    }

    [Fact]
    public void AddMongoDbProvider_Registers_MongoDbContext()
    {
        using var sp = BuildProvider();
        var ctx = sp.GetRequiredService<MongoDbContext>();
        Assert.NotNull(ctx);
    }

    [Fact]
    public void AddMongoDbProvider_Registers_CredentialProtector()
    {
        using var sp = BuildProvider();
        var protector = sp.GetRequiredService<CredentialProtector>();
        Assert.NotNull(protector);
    }

    [Fact]
    public void Without_MongoDbProvider_Uses_SqliteStores()
    {
        var services = new ServiceCollection();
        services.AddAgentCraftEngine();
        services.AddSqliteDataProvider();
        using var sp = services.BuildServiceProvider();

        var store = sp.GetRequiredService<IWorkflowStore>();
        Assert.IsNotType<MongoWorkflowStore>(store);
    }
}
