using System.Net;
using System.Net.Sockets;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// URL 安全驗證器 — 防止 SSRF（Server-Side Request Forgery）。
/// 阻擋對內網 IP、loopback、雲端 metadata 端點的請求。
/// </summary>
public static class SafeUrlValidator
{
    /// <summary>
    /// 驗證 URL 是否安全（非內網、非 loopback、非雲端 metadata）。
    /// 解析 DNS 後檢查實際 IP，防止 DNS rebinding。
    /// </summary>
    public static async Task<(bool IsSafe, string? Error)> ValidateAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return (false, "URL is empty");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return (false, $"Invalid URL: {url}");

        if (uri.Scheme is not ("http" or "https"))
            return (false, $"Unsupported scheme: {uri.Scheme}");

        var host = uri.Host;

        // 1. 直接檢查已知危險 host
        if (IsKnownDangerousHost(host))
            return (false, $"Blocked: {host} is not allowed (internal/metadata endpoint)");

        // 2. DNS 解析後檢查 IP
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            foreach (var ip in addresses)
            {
                if (IsPrivateOrReservedIp(ip))
                    return (false, $"Blocked: {host} resolves to private/reserved IP {ip}");
            }
        }
        catch (SocketException)
        {
            // DNS 解析失敗 — 放行（讓後續 HTTP 呼叫自己處理錯誤）
        }

        return (true, null);
    }

    /// <summary>同步版本（不做 DNS 解析，僅檢查 host 名稱）</summary>
    public static (bool IsSafe, string? Error) ValidateHost(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return (false, "URL is empty");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return (false, $"Invalid URL: {url}");

        if (uri.Scheme is not ("http" or "https"))
            return (false, $"Unsupported scheme: {uri.Scheme}");

        if (IsKnownDangerousHost(uri.Host))
            return (false, $"Blocked: {uri.Host} is not allowed");

        // 檢查 IP 字面值（例如 http://10.0.0.1/）
        if (IPAddress.TryParse(uri.Host, out var ip) && IsPrivateOrReservedIp(ip))
            return (false, $"Blocked: {uri.Host} is a private/reserved IP");

        return (true, null);
    }

    private static bool IsKnownDangerousHost(string host)
    {
        var lower = host.ToLowerInvariant();
        return lower is "localhost"
            or "metadata.google.internal"
            or "169.254.169.254"          // AWS/Azure/GCP metadata
            or "metadata.google.com"
            or "100.100.100.200";         // Alibaba Cloud metadata
    }

    internal static bool IsPrivateOrReservedIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;

        var bytes = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
        {
            return bytes[0] switch
            {
                10 => true,                                         // 10.0.0.0/8
                172 => bytes[1] >= 16 && bytes[1] <= 31,           // 172.16.0.0/12
                192 => bytes[1] == 168,                             // 192.168.0.0/16
                169 => bytes[1] == 254,                             // 169.254.0.0/16 (link-local)
                127 => true,                                        // 127.0.0.0/8
                0 => true,                                          // 0.0.0.0/8
                _ => false
            };
        }

        // IPv6 link-local, loopback
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;

        return false;
    }
}
