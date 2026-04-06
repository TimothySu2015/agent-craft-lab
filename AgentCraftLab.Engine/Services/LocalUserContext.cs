namespace AgentCraftLab.Engine.Services;

public class LocalUserContext : IUserContext
{
    public Task<string> GetUserIdAsync() => Task.FromResult("local");

    public Task<bool> IsAdminAsync() => Task.FromResult(true);

    public Task<string> GetDisplayNameAsync() => Task.FromResult("Local User");

    public Task<string> GetAvatarUrlAsync() => Task.FromResult("");

    public Task<string> GetEmailAsync() => Task.FromResult("");
}
