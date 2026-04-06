using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using DiffPlex;
using DiffPlex.Chunkers;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// Codebase 探索工具實作（List Directory, Read File, Search Code）。
/// 所有操作限制在 WorkingDirectory 範圍內，防止路徑穿越。
/// </summary>
internal static partial class ToolImplementations
{
    // === 常數限制 ===
    private const int MaxTreeDepth = 8;
    private const int MaxTreeEntries = 2000;
    private const int MaxReadLines = 500;
    private const long MaxReadFileSize = 5 * 1024 * 1024; // 5 MB
    private const int MaxSearchResults = 100;
    private const int MaxSearchContextLines = 5;
    private static readonly TimeSpan SearchRegexTimeout = TimeSpan.FromSeconds(3);
    private const int MaxLineLength = 1000;

    /// <summary>
    /// Codebase 探索的工作目錄。可透過 AddAgentCraftEngine(workingDir:) 設定。
    /// 預設為應用程式執行目錄。
    /// </summary>
    internal static string WorkingDirectory { get; set; } = AppContext.BaseDirectory;

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", ".git", ".vs", ".idea", "packages",
        "TestResults", "__pycache__", ".next", "dist", "build", "coverage", ".nuget"
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".pdb", ".obj", ".o", ".so", ".dylib", ".lib", ".a",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg", ".webp",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".zip", ".tar", ".gz", ".7z", ".rar", ".bz2",
        ".mp3", ".mp4", ".avi", ".mov", ".wav", ".flac", ".mkv",
        ".woff", ".woff2", ".ttf", ".eot", ".otf",
        ".class", ".pyc", ".pyo", ".wasm",
        ".db", ".sqlite", ".mdb", ".ldf", ".mdf",
        ".snk", ".pfx", ".cer"
    };

    /// <summary>
    /// 驗證路徑安全性：正規化路徑並確保在 WorkingDirectory 範圍內。
    /// </summary>
    private static (bool IsValid, string? Error, string ResolvedPath) ValidateReadPath(string inputPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                return (false, "Error: path is required.", "");
            }

            var resolved = Path.GetFullPath(inputPath, WorkingDirectory);

            // 正規化 WorkingDirectory 以確保一致的比較
            var normalizedBase = Path.GetFullPath(WorkingDirectory);
            if (!normalizedBase.EndsWith(Path.DirectorySeparatorChar))
            {
                normalizedBase += Path.DirectorySeparatorChar;
            }

            if (!resolved.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase) &&
                !resolved.Equals(normalizedBase.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"Error: access denied. Path is outside the allowed working directory.", "");
            }

            return (true, null, resolved);
        }
        catch (Exception ex)
        {
            return (false, $"Error: invalid path — {ex.Message}", "");
        }
    }

    /// <summary>
    /// 檢查檔案是否為二進位格式（副檔名白名單 + NUL byte fallback）。
    /// </summary>
    private static bool IsBinaryFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(ext) && BinaryExtensions.Contains(ext))
        {
            return true;
        }

        // Fallback: 讀取前 8KB 檢查 NUL byte
        try
        {
            var buffer = new byte[Math.Min(8192, new FileInfo(filePath).Length)];
            using var fs = File.OpenRead(filePath);
            var bytesRead = fs.Read(buffer, 0, buffer.Length);
            for (var i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0)
                {
                    return true;
                }
            }
        }
        catch
        {
            // 無法讀取時當作二進位
            return true;
        }

        return false;
    }

    /// <summary>
    /// 將簡易 glob pattern（*.cs, test?.txt）轉換為 Regex，用於檔名篩選。
    /// </summary>
    private static Regex? CompileGlobFilter(string pattern)
    {
        if (pattern == "*" || string.IsNullOrWhiteSpace(pattern)) return null;
        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return new Regex(regexPattern, RegexOptions.IgnoreCase, SearchRegexTimeout);
    }

    // ========== list_directory ==========

    [Description("列出目錄結構（tree 格式），用於探索 codebase 的檔案組織")]
    internal static string ListDirectory(
        [Description("相對於工作目錄的路徑，例如 src 或 .（根目錄），預設 .")] string directoryPath = ".",
        [Description("glob 篩選（僅匹配檔案名），例如 *.cs 或 *.ts，預設 * 表示全部")] string pattern = "*",
        [Description("遞迴深度，預設 3，上限 8")] int maxDepth = 3,
        [Description("是否顯示 . 開頭的隱藏項目，預設 false")] bool includeHidden = false)
    {
        try
        {
            var (isValid, error, resolvedPath) = ValidateReadPath(directoryPath);
            if (!isValid) return error!;

            if (!Directory.Exists(resolvedPath))
            {
                return $"Error: directory not found: {directoryPath}";
            }

            maxDepth = Math.Clamp(maxDepth, 1, MaxTreeDepth);

            var treeCtx = new TreeBuildContext
            {
                FileFilter = CompileGlobFilter(pattern),
                IncludeHidden = includeHidden,
                MaxDepth = maxDepth
            };
            BuildTree(resolvedPath, treeCtx, "", 0);

            if (treeCtx.EntryCount == 0)
            {
                return $"Directory '{directoryPath}' is empty or all entries are excluded.";
            }

            var header = $"\U0001F4C1 {directoryPath}/ ({treeCtx.TotalFiles} files, {treeCtx.TotalDirs} dirs)\n";
            treeCtx.Output.Insert(0, header);

            if (treeCtx.EntryCount >= MaxTreeEntries)
            {
                treeCtx.Output.AppendLine($"... (truncated at {MaxTreeEntries} entries)");
            }

            return treeCtx.Output.ToString();
        }
        catch (Exception ex)
        {
            return $"Error listing directory: {ex.Message}";
        }
    }

    private sealed class TreeBuildContext
    {
        public StringBuilder Output { get; } = new();
        public Regex? FileFilter { get; init; }
        public bool IncludeHidden { get; init; }
        public int MaxDepth { get; init; }
        public int EntryCount { get; set; }
        public int TotalFiles { get; set; }
        public int TotalDirs { get; set; }
    }

    private static void BuildTree(string dirPath, TreeBuildContext ctx, string indent, int currentDepth)
    {
        if (currentDepth >= ctx.MaxDepth || ctx.EntryCount >= MaxTreeEntries)
        {
            return;
        }

        // 取得子目錄
        List<string> subDirs;
        try
        {
            subDirs = Directory.GetDirectories(dirPath)
                .Where(d =>
                {
                    var name = Path.GetFileName(d);
                    if (!ctx.IncludeHidden && name.StartsWith('.')) return false;
                    return !ExcludedDirectories.Contains(name);
                })
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        // 取得檔案
        List<string> files;
        try
        {
            files = Directory.GetFiles(dirPath)
                .Where(f =>
                {
                    var name = Path.GetFileName(f);
                    if (!ctx.IncludeHidden && name.StartsWith('.')) return false;
                    if (ctx.FileFilter != null && !ctx.FileFilter.IsMatch(name)) return false;
                    return true;
                })
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        var entries = new List<(string Name, bool IsDir, string FullPath)>();
        foreach (var d in subDirs) entries.Add((Path.GetFileName(d) + "/", true, d));
        foreach (var f in files) entries.Add((Path.GetFileName(f), false, f));

        for (var i = 0; i < entries.Count; i++)
        {
            if (ctx.EntryCount >= MaxTreeEntries) break;

            var (name, isDir, fullPath) = entries[i];
            var isLast = i == entries.Count - 1;
            var connector = isLast ? "\u2514\u2500\u2500 " : "\u251C\u2500\u2500 ";
            var childIndent = indent + (isLast ? "    " : "\u2502   ");

            if (isDir)
            {
                ctx.Output.AppendLine($"{indent}{connector}{name}");
                ctx.EntryCount++;
                ctx.TotalDirs++;
                BuildTree(fullPath, ctx, childIndent, currentDepth + 1);
            }
            else
            {
                var size = FormatFileSize(fullPath);
                ctx.Output.AppendLine($"{indent}{connector}{name} ({size})");
                ctx.EntryCount++;
                ctx.TotalFiles++;
            }
        }
    }

    private static string FormatFileSize(string filePath)
    {
        try
        {
            var length = new FileInfo(filePath).Length;
            return length switch
            {
                < 1024 => $"{length} B",
                < 1024 * 1024 => $"{length / 1024.0:F1} KB",
                _ => $"{length / (1024.0 * 1024.0):F1} MB"
            };
        }
        catch
        {
            return "? KB";
        }
    }

    // ========== read_file ==========

    [Description("讀取檔案內容（帶行號），用於檢視原始碼")]
    internal static string ReadFile(
        [Description("檔案路徑（相對於工作目錄或絕對路徑）")] string filePath,
        [Description("起始行號（1-based），預設 1")] int startLine = 1,
        [Description("讀取行數，預設 200，上限 500")] int lineCount = 200)
    {
        try
        {
            var (isValid, error, resolvedPath) = ValidateReadPath(filePath);
            if (!isValid) return error!;

            if (!File.Exists(resolvedPath))
            {
                return $"Error: file not found: {filePath}";
            }

            var fileInfo = new FileInfo(resolvedPath);
            if (fileInfo.Length > MaxReadFileSize)
            {
                return $"Error: file too large ({fileInfo.Length / (1024.0 * 1024.0):F1} MB). Maximum: {MaxReadFileSize / (1024 * 1024)} MB.";
            }

            if (IsBinaryFile(resolvedPath))
            {
                return $"Error: '{Path.GetFileName(resolvedPath)}' is a binary file and cannot be displayed as text.";
            }

            startLine = Math.Max(1, startLine);
            lineCount = Math.Clamp(lineCount, 1, MaxReadLines);

            var encoding = DetectEncoding(resolvedPath);
            var sb = new StringBuilder();
            var totalLines = 0;
            var linesRead = 0;
            var currentLine = 0;

            using var reader = new StreamReader(resolvedPath, encoding, detectEncodingFromByteOrderMarks: true);
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (line is null) break;

                currentLine++;
                totalLines = currentLine;

                if (currentLine < startLine) continue;
                if (linesRead >= lineCount) continue; // 繼續計算 totalLines

                // 截斷超長行
                if (line.Length > MaxLineLength)
                {
                    line = string.Concat(line.AsSpan(0, MaxLineLength), "...(truncated)");
                }

                var lineNum = currentLine.ToString().PadLeft(6);
                sb.AppendLine($"{lineNum}: {line}");
                linesRead++;
            }

            if (linesRead == 0)
            {
                return $"Error: startLine {startLine} is beyond end of file ({totalLines} lines).";
            }

            var endLine = startLine + linesRead - 1;
            var sizeStr = FormatFileSize(resolvedPath);
            var header = $"File: {filePath} | Lines {startLine}-{endLine} of {totalLines} | Size: {sizeStr}\n";

            sb.Insert(0, header);

            var remaining = totalLines - endLine;
            if (remaining > 0)
            {
                sb.AppendLine($"(... {remaining} more lines)");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
        }
    }

    // ========== search_code ==========

    [Description("在 codebase 中搜尋匹配 regex pattern 的程式碼行")]
    internal static string SearchCode(
        [Description("搜尋起始目錄（相對於工作目錄），預設 .")] string directoryPath = ".",
        [Description("regex 搜尋 pattern，例如 class\\s+\\w+Service 或 TODO")] string pattern = "",
        [Description("檔案篩選 glob，例如 *.cs 或 *.ts，預設 * 表示全部")] string filePattern = "*",
        [Description("最大結果數，預設 30，上限 100")] int maxResults = 30,
        [Description("每個匹配的上下文行數，預設 2，上限 5")] int contextLines = 2)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return "Error: search pattern is required.";
            }

            var (isValid, error, resolvedPath) = ValidateReadPath(directoryPath);
            if (!isValid) return error!;

            if (!Directory.Exists(resolvedPath))
            {
                return $"Error: directory not found: {directoryPath}";
            }

            maxResults = Math.Clamp(maxResults, 1, MaxSearchResults);
            contextLines = Math.Clamp(contextLines, 0, MaxSearchContextLines);

            Regex searchRegex;
            try
            {
                searchRegex = new Regex(pattern, RegexOptions.IgnoreCase, SearchRegexTimeout);
            }
            catch (RegexParseException ex)
            {
                return $"Error: invalid regex pattern — {ex.Message}";
            }

            var searchCtx = new SearchContext
            {
                RootPath = resolvedPath,
                SearchRegex = searchRegex,
                FileFilter = CompileGlobFilter(filePattern),
                ContextLines = contextLines,
                MaxResults = maxResults
            };

            SearchDirectory(resolvedPath, searchCtx);

            if (searchCtx.Results.Count == 0)
            {
                return $"No matches found for '{pattern}' in {searchCtx.FilesSearched} files.";
            }

            var sb = new StringBuilder();

            // 按檔案分組輸出
            string? currentFile = null;
            foreach (var match in searchCtx.Results)
            {
                if (match.RelativePath != currentFile)
                {
                    currentFile = match.RelativePath;
                    sb.AppendLine();
                    sb.AppendLine($"=== {currentFile} ===");
                }

                foreach (var (lineNum, line, isMatch) in match.Lines)
                {
                    var prefix = isMatch ? "> " : "  ";
                    var displayLine = line.Length > MaxLineLength
                        ? string.Concat(line.AsSpan(0, MaxLineLength), "...(truncated)")
                        : line;
                    sb.AppendLine($"{prefix}{lineNum,5}: {displayLine}");
                }

                sb.AppendLine();
            }

            sb.AppendLine($"Found {searchCtx.Results.Count} matches in {searchCtx.FilesWithMatches.Count} files (searched {searchCtx.FilesSearched} files)");

            if (searchCtx.Results.Count >= maxResults)
            {
                sb.AppendLine($"(results limited to {maxResults}, use filePattern or refine pattern to narrow search)");
            }

            return sb.ToString();
        }
        catch (RegexMatchTimeoutException)
        {
            return $"Error: regex pattern timed out after {SearchRegexTimeout.TotalSeconds}s. Simplify the pattern.";
        }
        catch (Exception ex)
        {
            return $"Error searching code: {ex.Message}";
        }
    }

    private sealed class SearchContext
    {
        public required string RootPath { get; init; }
        public required Regex SearchRegex { get; init; }
        public Regex? FileFilter { get; init; }
        public int ContextLines { get; init; }
        public int MaxResults { get; init; }
        public List<SearchMatch> Results { get; } = [];
        public HashSet<string> FilesWithMatches { get; } = [];
        public int FilesSearched { get; set; }
    }

    private sealed record SearchMatch(string RelativePath, List<(int LineNum, string Line, bool IsMatch)> Lines);

    private static void SearchDirectory(string dirPath, SearchContext ctx)
    {
        if (ctx.Results.Count >= ctx.MaxResults) return;

        // 搜尋檔案
        try
        {
            foreach (var filePath in Directory.GetFiles(dirPath))
            {
                if (ctx.Results.Count >= ctx.MaxResults) return;

                var fileName = Path.GetFileName(filePath);
                if (fileName.StartsWith('.')) continue;
                if (ctx.FileFilter != null && !ctx.FileFilter.IsMatch(fileName)) continue;
                if (IsBinaryFile(filePath)) continue;

                // 跳過太大的檔案
                try
                {
                    if (new FileInfo(filePath).Length > MaxReadFileSize) continue;
                }
                catch
                {
                    continue;
                }

                ctx.FilesSearched++;
                SearchFile(filePath, ctx);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // 跳過無權限的目錄
        }

        // 遞迴子目錄
        try
        {
            foreach (var subDir in Directory.GetDirectories(dirPath))
            {
                if (ctx.Results.Count >= ctx.MaxResults) return;

                var dirName = Path.GetFileName(subDir);
                if (dirName.StartsWith('.')) continue;
                if (ExcludedDirectories.Contains(dirName)) continue;

                SearchDirectory(subDir, ctx);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // 跳過無權限的目錄
        }
    }

    private static void SearchFile(string filePath, SearchContext ctx)
    {
        try
        {
            var relativePath = Path.GetRelativePath(ctx.RootPath, filePath).Replace('\\', '/');
            var lastOutputLine = -1;

            // Circular buffer 保留前 N 行（用於 context 往回看）
            var bufferSize = ctx.ContextLines + 1;
            var buffer = new (int LineNum, string Line)[bufferSize];
            var pendingAfterLines = 0; // match 後還需要收集的 context 行數
            List<(int LineNum, string Line, bool IsMatch)>? pendingMatch = null;
            var lineIndex = 0;

            using var reader = new StreamReader(filePath, DetectEncoding(filePath), detectEncodingFromByteOrderMarks: true);
            while (reader.ReadLine() is { } line)
            {
                var lineNum = lineIndex + 1;

                // 寫入 circular buffer
                buffer[lineIndex % bufferSize] = (lineNum, line);

                // 如果有待收集的 after-context
                if (pendingMatch is not null && pendingAfterLines > 0)
                {
                    pendingMatch.Add((lineNum, line, false));
                    pendingAfterLines--;

                    if (pendingAfterLines == 0)
                    {
                        lastOutputLine = lineIndex;
                        ctx.Results.Add(new SearchMatch(relativePath, pendingMatch));
                        ctx.FilesWithMatches.Add(relativePath);
                        pendingMatch = null;
                        if (ctx.Results.Count >= ctx.MaxResults) return;
                    }
                }

                // 檢查是否 match（包含 after-context 剛結束的情況 — 不漏掉相鄰 match）
                if (pendingMatch is null && ctx.SearchRegex.IsMatch(line))
                {
                    var matchLines = new List<(int LineNum, string Line, bool IsMatch)>();

                    // Before-context：從 circular buffer 取前 N 行
                    var beforeStart = Math.Max(lastOutputLine + 1, lineIndex - ctx.ContextLines);
                    for (var j = beforeStart; j < lineIndex; j++)
                    {
                        var buffered = buffer[j % bufferSize];
                        if (buffered.Line is not null)
                        {
                            matchLines.Add((buffered.LineNum, buffered.Line, false));
                        }
                    }

                    // Match 行本身
                    matchLines.Add((lineNum, line, true));

                    // After-context：需要繼續讀後面的行
                    if (ctx.ContextLines > 0)
                    {
                        pendingMatch = matchLines;
                        pendingAfterLines = ctx.ContextLines;
                    }
                    else
                    {
                        lastOutputLine = lineIndex;
                        ctx.Results.Add(new SearchMatch(relativePath, matchLines));
                        ctx.FilesWithMatches.Add(relativePath);
                        if (ctx.Results.Count >= ctx.MaxResults) return;
                    }
                }

                lineIndex++;
            }

            // 檔案結束時，如果還有待輸出的 match（after-context 不足）
            if (pendingMatch is not null)
            {
                ctx.Results.Add(new SearchMatch(relativePath, pendingMatch));
                ctx.FilesWithMatches.Add(relativePath);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            throw;
        }
        catch
        {
            // 無法讀取的檔案直接跳過
        }
    }

    // ========== file_diff ==========

    /// <summary>
    /// 驗證檔案可供 diff 使用：路徑安全、存在、大小、非二進位。
    /// </summary>
    private static (bool IsValid, string? Error, string Text) ValidateAndReadFileForDiff(string filePath)
    {
        var (isValid, error, resolvedPath) = ValidateReadPath(filePath);
        if (!isValid) return (false, error, "");

        if (!File.Exists(resolvedPath))
            return (false, $"Error: file not found: {filePath}", "");

        var fileInfo = new FileInfo(resolvedPath);
        if (fileInfo.Length > MaxReadFileSize)
            return (false, $"Error: file too large: {filePath} ({fileInfo.Length / (1024.0 * 1024.0):F1} MB). Maximum: {MaxReadFileSize / (1024 * 1024)} MB.", "");

        if (IsBinaryFile(resolvedPath))
            return (false, $"Error: '{Path.GetFileName(resolvedPath)}' is a binary file.", "");

        var text = File.ReadAllText(resolvedPath, DetectEncoding(resolvedPath));
        return (true, null, text);
    }

    [Description("比較兩個檔案的差異，以 unified diff 格式輸出（類似 git diff）")]
    internal static string FileDiff(
        [Description("第一個檔案路徑（相對於工作目錄或絕對路徑）")] string filePath1,
        [Description("第二個檔案路徑（相對於工作目錄或絕對路徑）")] string filePath2,
        [Description("差異周圍的上下文行數，預設 3，上限 10")] int contextLines = 3,
        [Description("是否忽略空白差異（前後空白 + 連續空白），預設 false")] bool ignoreWhitespace = false)
    {
        try
        {
            var (ok1, err1, text1) = ValidateAndReadFileForDiff(filePath1);
            if (!ok1) return err1!;

            var (ok2, err2, text2) = ValidateAndReadFileForDiff(filePath2);
            if (!ok2) return err2!;

            return BuildUnifiedDiff(text1, text2, filePath1, filePath2, contextLines, ignoreWhitespace);
        }
        catch (Exception ex)
        {
            return $"Error comparing files: {ex.Message}";
        }
    }

    // ========== text_diff ==========

    [Description("比較兩段文字的差異，以 unified diff 格式輸出")]
    internal static string TextDiff(
        [Description("第一段文字（原始版本）")] string text1,
        [Description("第二段文字（修改版本）")] string text2,
        [Description("差異周圍的上下文行數，預設 3，上限 10")] int contextLines = 3,
        [Description("是否忽略空白差異（前後空白 + 連續空白），預設 false")] bool ignoreWhitespace = false)
    {
        try
        {
            if (text1 is null && text2 is null)
                return "Error: at least one of text1 or text2 must be provided.";

            return BuildUnifiedDiff(text1 ?? "", text2 ?? "", "original", "modified", contextLines, ignoreWhitespace);
        }
        catch (Exception ex)
        {
            return $"Error comparing text: {ex.Message}";
        }
    }

    // ========== 共用 Diff 核心 ==========

    private const int MaxDiffContextLines = 10;
    private const int MaxDiffOutputLines = 1000;
    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly LineChunker DiffLineChunker = new();

    private static string BuildUnifiedDiff(
        string text1, string text2, string label1, string label2,
        int contextLines, bool ignoreWhitespace)
    {
        contextLines = Math.Clamp(contextLines, 0, MaxDiffContextLines);

        if (ignoreWhitespace)
        {
            text1 = NormalizeWhitespace(text1);
            text2 = NormalizeWhitespace(text2);
        }

        var oldLines = text1.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var newLines = text2.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        var lineResult = new Differ().CreateDiffs(text1, text2, ignoreWhitespace, ignoreCase: false, DiffLineChunker);

        if (lineResult.DiffBlocks.Count == 0)
            return $"No differences found between '{label1}' and '{label2}'.";

        // 從 DiffBlocks 計算統計（單一來源）
        var (added, deleted, modified) = ComputeDiffStats(lineResult);

        var hunks = BuildHunks(lineResult, oldLines, newLines, contextLines);

        var sb = new StringBuilder();
        sb.AppendLine($"--- {label1}");
        sb.AppendLine($"+++ {label2}");

        var outputLines = 0;
        foreach (var hunk in hunks)
        {
            if (outputLines >= MaxDiffOutputLines)
            {
                sb.AppendLine($"... (output truncated at {MaxDiffOutputLines} lines)");
                break;
            }

            sb.AppendLine($"@@ -{hunk.OldStart + 1},{hunk.OldCount} +{hunk.NewStart + 1},{hunk.NewCount} @@");
            outputLines++;

            foreach (var line in hunk.Lines)
            {
                if (outputLines >= MaxDiffOutputLines)
                {
                    sb.AppendLine($"... (output truncated at {MaxDiffOutputLines} lines)");
                    break;
                }

                sb.AppendLine(line);
                outputLines++;
            }
        }

        sb.AppendLine();
        sb.AppendLine($"Summary: {added} added, {deleted} deleted, {modified} modified");

        return sb.ToString();
    }

    private static (int Added, int Deleted, int Modified) ComputeDiffStats(DiffPlex.Model.DiffResult lineResult)
    {
        var added = 0;
        var deleted = 0;
        var modified = 0;

        foreach (var block in lineResult.DiffBlocks)
        {
            if (block.DeleteCountA > 0 && block.InsertCountB > 0)
            {
                var common = Math.Min(block.DeleteCountA, block.InsertCountB);
                modified += common;
                deleted += block.DeleteCountA - common;
                added += block.InsertCountB - common;
            }
            else
            {
                deleted += block.DeleteCountA;
                added += block.InsertCountB;
            }
        }

        return (added, deleted, modified);
    }

    private sealed record DiffHunk(int OldStart, int OldCount, int NewStart, int NewCount, List<string> Lines);

    /// <summary>
    /// 從 DiffBlocks 建立 unified diff hunks，正確追蹤 old/new 行號。
    /// </summary>
    private static List<DiffHunk> BuildHunks(
        DiffPlex.Model.DiffResult lineResult, string[] oldLines, string[] newLines, int contextLines)
    {
        var hunks = new List<DiffHunk>();

        var oldIdx = 0;
        var newIdx = 0;
        var hunkBuilder = new HunkBuilder();
        var blockIdx = 0;

        while (oldIdx < oldLines.Length || newIdx < newLines.Length)
        {
            var currentBlock = blockIdx < lineResult.DiffBlocks.Count
                ? lineResult.DiffBlocks[blockIdx]
                : null;

            if (currentBlock != null && oldIdx == currentBlock.DeleteStartA)
            {
                // 進入 diff block — 開始或延伸 hunk
                if (!hunkBuilder.IsOpen)
                {
                    var ctxStart = Math.Max(0, oldIdx - contextLines);
                    var newCtxStart = Math.Max(0, newIdx - contextLines);
                    hunkBuilder.Open(ctxStart, newCtxStart);
                    for (var c = 0; c < oldIdx - ctxStart; c++)
                    {
                        hunkBuilder.AddContext(oldLines[ctxStart + c]);
                    }
                }

                for (var i = 0; i < currentBlock.DeleteCountA; i++)
                {
                    if (oldIdx + i < oldLines.Length)
                        hunkBuilder.AddDeletion(oldLines[oldIdx + i]);
                }

                for (var i = 0; i < currentBlock.InsertCountB; i++)
                {
                    if (currentBlock.InsertStartB + i < newLines.Length)
                        hunkBuilder.AddInsertion(newLines[currentBlock.InsertStartB + i]);
                }

                oldIdx += currentBlock.DeleteCountA;
                newIdx = currentBlock.InsertStartB + currentBlock.InsertCountB;
                blockIdx++;
                hunkBuilder.ResetTrailingContext();
            }
            else
            {
                // Unchanged 行
                if (hunkBuilder.IsOpen)
                {
                    hunkBuilder.IncrementTrailingContext();
                    if (hunkBuilder.TrailingContext <= contextLines && oldIdx < oldLines.Length)
                    {
                        hunkBuilder.AddContext(oldLines[oldIdx]);
                    }

                    var nextBlock = blockIdx < lineResult.DiffBlocks.Count
                        ? lineResult.DiffBlocks[blockIdx]
                        : null;
                    var gapToNext = nextBlock != null ? nextBlock.DeleteStartA - oldIdx - 1 : int.MaxValue;

                    if (hunkBuilder.TrailingContext >= contextLines && gapToNext > contextLines)
                    {
                        hunks.Add(hunkBuilder.Close());
                    }
                }

                oldIdx++;
                newIdx++;
            }
        }

        if (hunkBuilder.IsOpen)
        {
            hunks.Add(hunkBuilder.Close());
        }

        return hunks;
    }

    /// <summary>封裝 hunk 累積狀態，避免散落的 mutable 變數。</summary>
    private sealed class HunkBuilder
    {
        private readonly List<string> _lines = [];
        private int _oldStart;
        private int _newStart;
        private int _oldCount;
        private int _newCount;

        public bool IsOpen { get; private set; }
        public int TrailingContext { get; private set; }

        public void Open(int oldStart, int newStart)
        {
            _lines.Clear();
            _oldStart = oldStart;
            _newStart = newStart;
            _oldCount = 0;
            _newCount = 0;
            TrailingContext = 0;
            IsOpen = true;
        }

        public void AddContext(string line)
        {
            _lines.Add($" {line}");
            _oldCount++;
            _newCount++;
        }

        public void AddDeletion(string line)
        {
            _lines.Add($"-{line}");
            _oldCount++;
        }

        public void AddInsertion(string line)
        {
            _lines.Add($"+{line}");
            _newCount++;
        }

        public void ResetTrailingContext() => TrailingContext = 0;
        public void IncrementTrailingContext() => TrailingContext++;

        public DiffHunk Close()
        {
            IsOpen = false;
            return new DiffHunk(_oldStart, _oldCount, _newStart, _newCount, new List<string>(_lines));
        }
    }

    private static string NormalizeWhitespace(string text)
    {
        var lines = text.Split('\n');
        var sb = new StringBuilder(text.Length);
        foreach (var line in lines)
        {
            sb.AppendLine(MultiWhitespaceRegex.Replace(line.TrimEnd('\r').Trim(), " "));
        }
        return sb.ToString();
    }
}
