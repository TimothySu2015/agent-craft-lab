using System.Net;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

public class SafeUrlValidatorTests
{
    [Fact]
    public void ValidateHost_PublicUrl_Safe()
    {
        var (safe, _) = SafeUrlValidator.ValidateHost("https://example.com/api");
        Assert.True(safe);
    }

    [Fact]
    public void ValidateHost_Localhost_Blocked()
    {
        var (safe, error) = SafeUrlValidator.ValidateHost("http://localhost:8080");
        Assert.False(safe);
        Assert.Contains("Blocked", error);
    }

    [Fact]
    public void ValidateHost_PrivateIp_10_Blocked()
    {
        var (safe, _) = SafeUrlValidator.ValidateHost("http://10.0.0.1/internal");
        Assert.False(safe);
    }

    [Fact]
    public void ValidateHost_PrivateIp_172_Blocked()
    {
        var (safe, _) = SafeUrlValidator.ValidateHost("http://172.16.0.1/admin");
        Assert.False(safe);
    }

    [Fact]
    public void ValidateHost_PrivateIp_192_Blocked()
    {
        var (safe, _) = SafeUrlValidator.ValidateHost("http://192.168.1.1/router");
        Assert.False(safe);
    }

    [Fact]
    public void ValidateHost_CloudMetadata_Blocked()
    {
        var (safe, _) = SafeUrlValidator.ValidateHost("http://169.254.169.254/latest/meta-data/");
        Assert.False(safe);
    }

    [Fact]
    public void ValidateHost_Loopback127_Blocked()
    {
        var (safe, _) = SafeUrlValidator.ValidateHost("http://127.0.0.1:3000");
        Assert.False(safe);
    }

    [Fact]
    public void ValidateHost_FtpScheme_Blocked()
    {
        var (safe, error) = SafeUrlValidator.ValidateHost("ftp://files.example.com");
        Assert.False(safe);
        Assert.Contains("scheme", error);
    }

    [Fact]
    public void ValidateHost_EmptyUrl_Blocked()
    {
        var (safe, _) = SafeUrlValidator.ValidateHost("");
        Assert.False(safe);
    }

    [Fact]
    public void ValidateHost_172_NonPrivate_Safe()
    {
        // 172.32.0.1 is NOT in 172.16-31 range
        var (safe, _) = SafeUrlValidator.ValidateHost("http://172.32.0.1/api");
        Assert.True(safe);
    }

    [Fact]
    public void IsPrivateOrReservedIp_LinkLocal_True()
    {
        Assert.True(SafeUrlValidator.IsPrivateOrReservedIp(IPAddress.Parse("169.254.1.1")));
    }

    [Fact]
    public void IsPrivateOrReservedIp_PublicIp_False()
    {
        Assert.False(SafeUrlValidator.IsPrivateOrReservedIp(IPAddress.Parse("8.8.8.8")));
    }
}
