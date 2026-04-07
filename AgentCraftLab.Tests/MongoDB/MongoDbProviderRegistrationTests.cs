using AgentCraftLab.Engine.Data;
using AgentCraftLab.Engine.Extensions;
using AgentCraftLab.MongoDB;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Tests.MongoDB;

public class MongoDbProviderRegistrationTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddAgentCraftEngine();
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
    public void AddMongoDbProvider_Replaces_Store(Type interfaceType, Type expectedType)
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
    public void Without_MongoDbProvider_Uses_SqliteStores()
    {
        var services = new ServiceCollection();
        services.AddAgentCraftEngine();
        using var sp = services.BuildServiceProvider();

        var store = sp.GetRequiredService<IWorkflowStore>();
        Assert.IsNotType<MongoWorkflowStore>(store);
    }
}
