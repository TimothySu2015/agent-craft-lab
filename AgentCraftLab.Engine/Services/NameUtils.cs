using System.Text.RegularExpressions;

namespace AgentCraftLab.Engine.Services;

internal static class NameUtils
{
    public static string Sanitize(string name) =>
        Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]", "_").Trim('_');
}
