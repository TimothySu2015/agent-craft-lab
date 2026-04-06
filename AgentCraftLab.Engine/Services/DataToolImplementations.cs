using System.ComponentModel;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 資料類工具實作（JSON Parser, CSV Log Analyzer, ZIP Extractor, Write File, Write CSV）。
/// </summary>
internal static partial class ToolImplementations
{
    private const int MaxCsvContentLength = 50_000;
    private const int MaxCsvFiles = 100;

    /// <summary>
    /// Email 收件人白名單 — 只允許白名單內的收件人。支援 *@domain.com 萬用字元。
    /// 空清單 = 全部阻擋（預設安全）。由 AddAgentCraftEngine 從 config 注入。
    /// </summary>
    internal static List<string> EmailWhitelist { get; set; } = [];

    private static bool IsEmailAllowed(string email)
    {
        if (EmailWhitelist.Count == 0) return false;

        var normalized = email.Trim().ToLowerInvariant();
        foreach (var pattern in EmailWhitelist)
        {
            var p = pattern.Trim().ToLowerInvariant();
            if (p == normalized) return true;
            if (p.StartsWith('*') && normalized.EndsWith(p[1..])) return true;
        }
        return false;
    }

    /// <summary>路徑安全檢查 — 限制在 WorkingDirectory 範圍內（複用 CodeExplorer 的邏輯）。</summary>
    private static bool IsPathSafe(string inputPath)
    {
        try
        {
            var resolved = Path.GetFullPath(inputPath, WorkingDirectory);
            var normalizedBase = Path.GetFullPath(WorkingDirectory);
            if (!normalizedBase.EndsWith(Path.DirectorySeparatorChar))
                normalizedBase += Path.DirectorySeparatorChar;
            return resolved.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
    private const int MaxExtractedFiles = 500;
    private const long MaxZipSize = 100 * 1024 * 1024; // 100 MB
    private const long MaxWriteFileSize = 10 * 1024 * 1024; // 10 MB
    private const int MaxCsvRows = 10_000;

    private static readonly HashSet<string> AllowedWriteExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv", ".json", ".txt", ".md", ".xml", ".log", ".yaml", ".yml", ".html", ".tsv"
    };

    /// <summary>
    /// 檔案寫入的輸出目錄。可透過 appsettings.json 的 AgentCraft:OutputDir 設定。
    /// 預設為執行目錄下的 output/ 資料夾。
    /// </summary>
    internal static string OutputDirectory { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "output");

    /// <summary>
    /// 驗證檔名安全性並解析輸出路徑。禁止路徑穿越和非白名單副檔名。
    /// </summary>
    private static (bool IsValid, string? Error, string FilePath) ValidateAndResolveOutput(
        string fileName, string? requiredExtension = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return (false, "Error: fileName is required.", "");

        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            fileName.Contains("..") || Path.IsPathRooted(fileName))
            return (false, "Error: fileName must be a simple file name without path separators or '..'.", "");

        var ext = Path.GetExtension(fileName);
        if (requiredExtension is not null)
        {
            if (!fileName.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
                return (false, $"Error: fileName must end with {requiredExtension}", "");
        }
        else if (string.IsNullOrEmpty(ext) || !AllowedWriteExtensions.Contains(ext))
        {
            return (false, $"Error: unsupported file extension '{ext}'. Allowed: {string.Join(", ", AllowedWriteExtensions)}", "");
        }

        Directory.CreateDirectory(OutputDirectory);
        return (true, null, Path.Combine(OutputDirectory, fileName));
    }

    [Description("將文字內容寫入檔案（支援 .csv, .json, .txt, .md, .xml, .log, .yaml, .html, .tsv）")]
    internal static string WriteFile(
        [Description("檔案名稱（例如 report.txt），會寫入輸出目錄")] string fileName,
        [Description("要寫入的文字內容")] string content,
        [Description("寫入模式：overwrite（覆寫）或 append（追加），預設 overwrite")] string mode = "overwrite")
    {
        try
        {
            var (isValid, error, filePath) = ValidateAndResolveOutput(fileName);
            if (!isValid) return error!;

            if (string.IsNullOrEmpty(content))
                return "Error: content is empty.";

            if (content.Length > MaxWriteFileSize)
                return $"Error: content too large ({content.Length / (1024 * 1024.0):F1} MB). Maximum: {MaxWriteFileSize / (1024 * 1024)} MB.";

            if (mode.Equals("append", StringComparison.OrdinalIgnoreCase))
                File.AppendAllText(filePath, content, Encoding.UTF8);
            else
                File.WriteAllText(filePath, content, Encoding.UTF8);

            var info = new FileInfo(filePath);
            return $"Written to: {filePath} ({info.Length / 1024.0:F1} KB, mode: {mode})";
        }
        catch (Exception ex)
        {
            return $"Error writing file: {ex.Message}";
        }
    }

    [Description("將 JSON 陣列資料寫入 CSV 檔案（自動產生標頭、處理逗號/引號/換行的 escaping）")]
    internal static string WriteCsv(
        [Description("CSV 檔案名稱（例如 report.csv），會寫入輸出目錄")] string fileName,
        [Description("JSON 陣列字串，每個物件的 key 為欄位名，例如 [{\"Name\":\"Alice\",\"Score\":95}]")] string jsonData,
        [Description("是否追加到既有檔案（true = 追加且不寫標頭，false = 覆寫），預設 false")] bool append = false)
    {
        try
        {
            var (isValid, error, filePath) = ValidateAndResolveOutput(fileName, ".csv");
            if (!isValid) return error!;

            if (string.IsNullOrWhiteSpace(jsonData))
                return "Error: jsonData is required.";

            // 解析 JSON 陣列
            JsonElement[] rows;
            try
            {
                var parsed = JsonSerializer.Deserialize<JsonElement>(jsonData);
                if (parsed.ValueKind != JsonValueKind.Array)
                    return "Error: jsonData must be a JSON array, e.g. [{\"Name\":\"Alice\"}]";

                rows = parsed.EnumerateArray().ToArray();
            }
            catch (JsonException ex)
            {
                return $"Error: invalid JSON — {ex.Message}";
            }

            if (rows.Length == 0)
                return "Error: JSON array is empty.";

            if (rows.Length > MaxCsvRows)
                return $"Error: too many rows ({rows.Length}). Maximum: {MaxCsvRows}.";

            // 從第一筆物件收集所有欄位名作為 headers
            if (rows[0].ValueKind != JsonValueKind.Object)
                return "Error: each element in the JSON array must be an object.";

            var headers = rows[0].EnumerateObject().Select(p => p.Name).ToList();

            var sb = new StringBuilder();

            // 標頭（追加模式且檔案已存在時不寫標頭）
            if (!append || !File.Exists(filePath))
                sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            // 資料列
            foreach (var row in rows)
            {
                var values = headers.Select(header =>
                {
                    if (!row.TryGetProperty(header, out var prop)) return "";
                    return prop.ValueKind switch
                    {
                        JsonValueKind.String => prop.GetString() ?? "",
                        JsonValueKind.Null => "",
                        _ => prop.GetRawText()
                    };
                }).Select(CsvEscape);

                sb.AppendLine(string.Join(",", values));
            }

            if (append)
                File.AppendAllText(filePath, sb.ToString(), Encoding.UTF8);
            else
                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

            var info = new FileInfo(filePath);
            return $"Written {rows.Length} rows to: {filePath} ({info.Length / 1024.0:F1} KB, headers: {string.Join(", ", headers)})";
        }
        catch (Exception ex)
        {
            return $"Error writing CSV: {ex.Message}";
        }
    }

    /// <summary>
    /// RFC 4180 CSV escaping：欄位含逗號、引號、換行時加雙引號包裹，內部引號雙倍化。
    /// </summary>
    private static string CsvEscape(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }

    [Description("解壓縮 ZIP 檔案到暫存目錄，回傳解壓後的目錄路徑與檔案清單")]
    internal static string ExtractZip(
        [Description("ZIP 檔案的完整路徑")] string zipFilePath)
    {
        try
        {
            if (!IsPathSafe(zipFilePath))
            {
                return "Error: path must be within the working directory.";
            }

            if (!File.Exists(zipFilePath))
            {
                return $"File not found: {zipFilePath}";
            }

            if (!zipFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return "Only .zip files are supported.";
            }

            var fileInfo = new FileInfo(zipFilePath);
            if (fileInfo.Length > MaxZipSize)
            {
                return $"ZIP file too large: {fileInfo.Length / (1024 * 1024)}MB. Maximum: {MaxZipSize / (1024 * 1024)}MB.";
            }

            var extractDir = Path.Combine(
                WorkingDirectory,
                Models.TempPaths.ZipFolder,
                $"{Guid.NewGuid():N}_extracted");
            Directory.CreateDirectory(extractDir);

            ZipFile.ExtractToDirectory(zipFilePath, extractDir);

            var files = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
            var sb = new StringBuilder();
            sb.AppendLine($"Successfully extracted to: {extractDir}");
            sb.AppendLine($"Total files: {files.Length}");
            sb.AppendLine();

            var displayCount = Math.Min(files.Length, MaxExtractedFiles);
            foreach (var file in files.Take(displayCount))
            {
                var info = new FileInfo(file);
                var relativePath = Path.GetRelativePath(extractDir, file);
                sb.AppendLine($"  {relativePath}  ({info.Length / 1024.0:F1} KB)");
            }

            if (files.Length > MaxExtractedFiles)
            {
                sb.AppendLine($"  ... and {files.Length - MaxExtractedFiles} more files");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"ZIP extraction failed: {ex.Message}";
        }
    }

    [Description("讀取指定目錄下所有 CSV 檔案的內容，合併回傳供 AI 分析 Log")]
    internal static string ReadCsvLogs(
        [Description("要掃描的目錄路徑，例如 C:\\Logs")] string directoryPath,
        [Description("檔名篩選 pattern（支援萬用字元），例如 *error* 或 2026-03*，預設 * 表示全部")] string pattern = "*",
        [Description("每個檔案最多讀取的資料列數（不含標頭），0 表示不限，預設 200")] int maxRowsPerFile = 200,
        [Description("是否遞迴掃描子目錄，預設 false")] bool includeSubfolders = false)
    {
        try
        {
            // 路徑安全檢查 — 限制在 WorkingDirectory 範圍內
            if (!IsPathSafe(directoryPath))
            {
                return $"Error: path must be within the working directory.";
            }

            if (!Directory.Exists(directoryPath))
            {
                return $"Directory not found: {directoryPath}";
            }

            var searchPattern = pattern.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                ? pattern
                : $"{pattern}.csv";

            var searchOption = includeSubfolders
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            var csvFiles = Directory.GetFiles(directoryPath, searchPattern, searchOption)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Take(MaxCsvFiles)
                .ToList();

            if (csvFiles.Count == 0)
            {
                return $"No CSV files matching '{searchPattern}' found in: {directoryPath}";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== Found {csvFiles.Count} CSV file(s) in: {directoryPath} ===");
            sb.AppendLine();

            int fileIndex = 0;
            int totalRows = 0;
            bool contentLimitReached = false;

            foreach (var file in csvFiles)
            {
                if (contentLimitReached)
                {
                    break;
                }

                fileIndex++;
                var info = new FileInfo(file);
                var relativePath = Path.GetRelativePath(directoryPath, file);
                sb.AppendLine($"--- [{fileIndex}/{csvFiles.Count}] {relativePath} ({info.Length / 1024.0:F1} KB) ---");

                int lineCount = 0;
                int dataRowCount = 0;
                int fileDataRows = 0;

                using var reader = new StreamReader(file, DetectEncoding(file), detectEncodingFromByteOrderMarks: true);

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (line is null)
                    {
                        break;
                    }

                    lineCount++;

                    // 第一行視為標頭，一律輸出
                    if (lineCount == 1)
                    {
                        sb.AppendLine(line);
                        continue;
                    }

                    fileDataRows++;

                    if (maxRowsPerFile > 0 && dataRowCount >= maxRowsPerFile)
                    {
                        continue;
                    }

                    if (sb.Length + line.Length > MaxCsvContentLength)
                    {
                        contentLimitReached = true;
                        break;
                    }

                    sb.AppendLine(line);
                    dataRowCount++;
                }

                // 計算剩餘行數
                if (!contentLimitReached && (maxRowsPerFile > 0 && dataRowCount < fileDataRows))
                {
                    while (!reader.EndOfStream)
                    {
                        reader.ReadLine();
                        fileDataRows++;
                    }
                }

                if (dataRowCount < fileDataRows)
                {
                    sb.AppendLine($"... (showing {dataRowCount} of {fileDataRows} data rows)");
                }

                totalRows += fileDataRows > 0 ? fileDataRows : dataRowCount;
                sb.AppendLine();
            }

            if (contentLimitReached)
            {
                sb.AppendLine($"(Content truncated: reached {MaxCsvContentLength / 1000}K character limit, remaining files skipped)");
                sb.AppendLine();
            }

            sb.AppendLine($"=== Summary: {fileIndex} file(s) processed, {totalRows:N0} total data rows ===");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading CSV logs: {ex.Message}";
        }
    }

    private const int MaxRecipients = 10;
    private const int MaxAttachmentSize = 20 * 1024 * 1024; // 20 MB

    [Description("透過 SMTP 發送電子郵件，支援附件")]
    internal static async Task<string> SendEmailAsync(
        [Description("收件人 Email（多人以逗號分隔）")] string to,
        [Description("郵件主旨")] string subject,
        [Description("郵件內容")] string body,
        string smtpHost,
        int smtpPort,
        string fromEmail,
        string password,
        [Description("副本收件人（多人以逗號分隔），可留空")] string cc = "",
        [Description("是否為 HTML 格式，預設 false")] bool isHtml = false,
        [Description("附件檔案路徑（多個以逗號分隔），或目錄路徑（會附加目錄下所有檔案），可留空")] string attachments = "")
    {
        var attachmentStreams = new List<Stream>();
        try
        {
            if (string.IsNullOrWhiteSpace(to))
            {
                return "Error: recipient (to) is required.";
            }

            var recipients = to.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var ccList = string.IsNullOrWhiteSpace(cc)
                ? []
                : cc.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // Email 白名單檢查 — 所有收件人（含 CC）都必須在白名單內
            var blockedRecipients = recipients.Concat(ccList)
                .Where(r => !IsEmailAllowed(r))
                .ToList();
            if (blockedRecipients.Count > 0)
            {
                return $"[Blocked] Recipients not in whitelist: {string.Join(", ", blockedRecipients)}. Configure EmailWhitelist in appsettings.json.";
            }

            if (recipients.Length + ccList.Length > MaxRecipients)
            {
                return $"Error: too many recipients. Maximum {MaxRecipients} (to + cc combined).";
            }

            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(fromEmail));
            foreach (var addr in recipients)
            {
                message.To.Add(MailboxAddress.Parse(addr));
            }
            foreach (var addr in ccList)
            {
                message.Cc.Add(MailboxAddress.Parse(addr));
            }
            message.Subject = subject;

            var textPart = new TextPart(isHtml ? "html" : "plain") { Text = body };
            var attachmentFiles = ResolveAttachmentPaths(attachments);

            if (attachmentFiles.Count > 0)
            {
                var multipart = new Multipart("mixed") { textPart };
                long totalSize = 0;
                foreach (var filePath in attachmentFiles)
                {
                    totalSize += new FileInfo(filePath).Length;
                    if (totalSize > MaxAttachmentSize)
                    {
                        return $"Error: total attachment size exceeds {MaxAttachmentSize / (1024 * 1024)}MB limit.";
                    }

                    var stream = File.OpenRead(filePath);
                    attachmentStreams.Add(stream);
                    multipart.Add(new MimePart(MimeTypes.GetMimeType(filePath))
                    {
                        Content = new MimeContent(stream),
                        ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                        ContentTransferEncoding = ContentEncoding.Base64,
                        FileName = Path.GetFileName(filePath)
                    });
                }
                message.Body = multipart;
            }
            else
            {
                message.Body = textPart;
            }

            using var client = new SmtpClient();
            var sslOption = smtpPort switch
            {
                465 => SecureSocketOptions.SslOnConnect,
                25 => SecureSocketOptions.None,
                _ => SecureSocketOptions.StartTls
            };
            await client.ConnectAsync(smtpHost, smtpPort, sslOption);
            if (!string.IsNullOrEmpty(password))
            {
                await client.AuthenticateAsync(fromEmail, password);
            }
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            var recipientSummary = string.Join(", ", recipients);
            var ccSummary = ccList.Length > 0 ? $", CC: {string.Join(", ", ccList)}" : "";
            var attachSummary = attachmentFiles.Count > 0 ? $"\nAttachments: {attachmentFiles.Count} file(s)" : "";
            return $"Email sent successfully.\nTo: {recipientSummary}{ccSummary}\nSubject: {subject}{attachSummary}";
        }
        catch (Exception ex)
        {
            return $"Failed to send email: {ex.Message}";
        }
        finally
        {
            foreach (var stream in attachmentStreams)
            {
                stream.Dispose();
            }
        }
    }

    /// <summary>
    /// 解析附件路徑：支援逗號分隔的檔案路徑，或單一目錄路徑（會展開為目錄下所有檔案）。
    /// </summary>
    private static List<string> ResolveAttachmentPaths(string attachments)
    {
        if (string.IsNullOrWhiteSpace(attachments))
        {
            return [];
        }

        var paths = attachments.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<string>();

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                result.AddRange(Directory.GetFiles(path, "*", SearchOption.AllDirectories));
            }
            else if (File.Exists(path))
            {
                result.Add(path);
            }
        }

        return result;
    }

    /// <summary>
    /// 偵測檔案編碼：有 BOM 就用 BOM，否則嘗試判斷是否為 UTF-8，fallback 到系統預設編碼（Windows 繁中為 Big5）。
    /// </summary>
    private const int EncodingSampleSize = 8192;

    private static Encoding DetectEncoding(string filePath)
    {
        var bufferSize = (int)Math.Min(new FileInfo(filePath).Length, EncodingSampleSize);
        var buffer = new byte[bufferSize];
        using (var fs = File.OpenRead(filePath))
        {
            _ = fs.Read(buffer, 0, bufferSize);
        }

        // BOM 偵測
        if (bufferSize >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF) return Encoding.UTF8;
        if (bufferSize >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE) return Encoding.Unicode;        // UTF-16 LE
        if (bufferSize >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF) return Encoding.BigEndianUnicode; // UTF-16 BE

        // 無 BOM：嘗試 UTF-8 解碼，若失敗則 fallback 到 Big5
        try
        {
            var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            utf8Strict.GetString(buffer);
            return Encoding.UTF8;
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(950); // Big5
        }
    }
}
