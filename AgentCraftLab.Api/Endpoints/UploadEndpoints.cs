using System.Collections.Concurrent;

namespace AgentCraftLab.Api.Endpoints;

/// <summary>
/// 暫存檔案上傳 — 執行 workflow 時從暫存區取出注入 Attachment。
/// </summary>
public static class UploadEndpoints
{
    private const long MaxFileSize = 32 * 1024 * 1024; // 32 MB
    private static readonly ConcurrentDictionary<string, UploadedFile> TempFiles = new();

    public static void MapUploadEndpoints(this WebApplication app)
    {
        app.MapPost("/api/upload", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new ApiError("FILE_REQUIRED", "No file uploaded"));
            }

            if (file.Length > MaxFileSize)
            {
                return Results.BadRequest(new ApiError("FILE_TOO_LARGE", Params: new() { ["maxMb"] = "32" }));
            }

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);

            var id = $"upload-{Guid.NewGuid():N}";
            TempFiles[id] = new UploadedFile(file.FileName, file.ContentType ?? "application/octet-stream", ms.ToArray(), DateTime.UtcNow);

            // 清理 1 小時前的暫存檔
            CleanupOldFiles();

            return Results.Ok(new { fileId = id, fileName = file.FileName, size = file.Length });
        }).DisableAntiforgery();
    }

    /// <summary>根據 fileId 取出暫存檔案（供 AG-UI 端點使用）。</summary>
    public static UploadedFile? GetAndRemove(string fileId)
    {
        return TempFiles.TryRemove(fileId, out var file) ? file : null;
    }

    private static void CleanupOldFiles()
    {
        var cutoff = DateTime.UtcNow.AddHours(-1);
        foreach (var kvp in TempFiles)
        {
            if (kvp.Value.UploadedAt < cutoff)
            {
                TempFiles.TryRemove(kvp.Key, out _);
            }
        }
    }
}

public record UploadedFile(string FileName, string ContentType, byte[] Data, DateTime UploadedAt);
