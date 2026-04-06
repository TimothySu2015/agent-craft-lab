namespace AgentCraftLab.Engine.Services;

public interface IUserContext
{
    Task<string> GetUserIdAsync();
    Task<bool> IsAdminAsync();
    Task<string> GetDisplayNameAsync();
    Task<string> GetAvatarUrlAsync();
    Task<string> GetEmailAsync();
}
