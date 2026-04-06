namespace AgentCraftLab.Engine.Services;

internal static class StringUtils
{
    public static string Truncate(string value, int maxLength, string suffix = "...") =>
        value.Length <= maxLength ? value : value[..maxLength] + suffix;
}
