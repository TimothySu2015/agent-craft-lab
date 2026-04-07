using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AgentCraftLab.Script;

/// <summary>
/// Roslyn C# 腳本安全掃描器 — 編譯前用 SyntaxTree 分析 AST，攔截危險 API。
/// 採用黑名單 namespace/型別 + 白名單 References 雙重防護。
/// </summary>
public static class RoslynCodeSanitizer
{
    /// <summary>禁止使用的 namespace 前綴。</summary>
    private static readonly string[] BlockedNamespaces =
    [
        "System.IO",
        "System.Net",
        "System.Diagnostics",
        "System.Reflection",
        "System.Runtime.InteropServices",
        "System.Runtime.Loader",
        "System.Security",
        "System.Threading.Thread",
        "Microsoft.Win32",
    ];

    /// <summary>禁止使用的型別名稱（簡稱或完整名稱）。</summary>
    private static readonly HashSet<string> BlockedTypes =
    [
        "File",
        "Directory",
        "FileInfo",
        "DirectoryInfo",
        "StreamReader",
        "StreamWriter",
        "FileStream",
        "Process",
        "ProcessStartInfo",
        "Assembly",
        "AppDomain",
        "Activator",
        "HttpClient",
        "WebClient",
        "Socket",
        "TcpClient",
        "UdpClient",
        "Thread",
        "Environment",
        "Registry",
        "RegistryKey",
        "GC",
    ];

    /// <summary>禁止使用的方法名稱。</summary>
    private static readonly HashSet<string> BlockedMethods =
    [
        "GetType",
        "InvokeMember",
        "DynamicInvoke",
    ];

    /// <summary>
    /// 掃描 C# 程式碼是否包含危險 API。回傳 null 表示安全，否則回傳錯誤訊息。
    /// </summary>
    public static string? Scan(string wrappedCode)
    {
        var tree = CSharpSyntaxTree.ParseText(wrappedCode);
        var root = tree.GetCompilationUnitRoot();

        // 單次遍歷 AST，用 switch 分派各種節點類型
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case UsingDirectiveSyntax usingDirective:
                {
                    var ns = usingDirective.Name?.ToString() ?? "";
                    if (IsBlockedNamespace(ns))
                    {
                        return $"Blocked namespace: '{ns}' is not allowed in sandbox.";
                    }
                    break;
                }

                case IdentifierNameSyntax identifier:
                {
                    var name = identifier.Identifier.Text;
                    if (BlockedTypes.Contains(name))
                    {
                        return $"Blocked type: '{name}' is not allowed in sandbox.";
                    }
                    if (BlockedMethods.Contains(name) && identifier.Parent is MemberAccessExpressionSyntax)
                    {
                        return $"Blocked method: '{name}' is not allowed in sandbox.";
                    }
                    break;
                }

                case MemberAccessExpressionSyntax memberAccess:
                {
                    var fullName = memberAccess.ToString();
                    if (IsBlockedNamespace(fullName))
                    {
                        return $"Blocked access: '{fullName}' is not allowed in sandbox.";
                    }
                    break;
                }

                case TypeOfExpressionSyntax typeofExpr:
                {
                    var typeName = typeofExpr.Type.ToString();
                    if (BlockedTypes.Any(bt => typeName.Contains(bt)))
                    {
                        return $"Blocked typeof: '{typeName}' is not allowed in sandbox.";
                    }
                    break;
                }
            }
        }

        return null;
    }

    private static bool IsBlockedNamespace(string name)
        => BlockedNamespaces.Any(bn => name.StartsWith(bn, StringComparison.Ordinal));
}
