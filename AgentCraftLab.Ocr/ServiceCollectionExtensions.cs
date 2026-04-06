using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TesseractOCR.Enums;
using TessEngine = TesseractOCR.Engine;

namespace AgentCraftLab.Ocr;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 註冊 Tesseract OCR 引擎。啟動時驗證 native library 可用性，失敗則跳過不註冊。
    /// 呼叫後需在 app build 完成後呼叫 <see cref="UseOcrTools"/> 完成工具掛載。
    /// </summary>
    /// <param name="services">DI 容器</param>
    /// <param name="tessDataPath">tessdata 目錄路徑</param>
    /// <param name="defaultLanguages">預設語言（"+" 分隔），預設 "chi_tra+chi_sim+eng+jpn+kor"</param>
    public static IServiceCollection AddOcr(
        this IServiceCollection services,
        string tessDataPath,
        string defaultLanguages = "chi_tra+chi_sim+eng+jpn+kor")
    {
        // 驗證 Tesseract native library 是否可用
        if (!VerifyTesseractAvailable(tessDataPath))
        {
            return services;
        }

        services.AddSingleton<IOcrEngine>(new TesseractOcrEngine(tessDataPath, defaultLanguages));
        return services;
    }

    /// <summary>
    /// 將 OCR 工具掛載到 ToolRegistryService。在 app build 完成後呼叫。
    /// </summary>
    /// <param name="provider">DI 容器</param>
    /// <param name="workingDirectory">工作目錄（與 Engine 的 WorkingDirectory 一致，用於路徑安全驗證）</param>
    public static void UseOcrTools(this IServiceProvider provider, string? workingDirectory = null)
    {
        var ocrEngine = provider.GetService<IOcrEngine>();
        if (ocrEngine is null)
        {
            return;
        }

        var registry = provider.GetRequiredService<ToolRegistryService>();
        var workDir = workingDirectory ?? AppContext.BaseDirectory;
        registry.RegisterOcrTools(ocrEngine, workDir);

        var logger = provider.GetService<ILogger<TesseractOcrEngine>>();
        logger?.LogInformation("OCR tool registered (tessdata: {Path})", workDir);
    }

    /// <summary>
    /// 嘗試建立 Tesseract Engine 驗證 native library 和 traineddata 是否可用。
    /// </summary>
    private static bool VerifyTesseractAvailable(string tessDataPath)
    {
        try
        {
            // 用 eng 做最小驗證（eng.traineddata 最常見）
            var testLang = "eng";
            var engPath = Path.Combine(tessDataPath, $"{testLang}.traineddata");
            if (!File.Exists(engPath))
            {
                // 找任何一個 .traineddata 檔案做驗證
                var anyFile = Directory.GetFiles(tessDataPath, "*.traineddata").FirstOrDefault();
                if (anyFile is null)
                {
                    Console.WriteLine($"[OCR] Skipped: no .traineddata files found in {tessDataPath}");
                    return false;
                }
                testLang = Path.GetFileNameWithoutExtension(anyFile);
            }

            using var engine = new TessEngine(tessDataPath, testLang, EngineMode.Default);
            Console.WriteLine($"[OCR] Tesseract {engine.Version} ready ({tessDataPath})");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OCR] Skipped: Tesseract native library not available — {ex.Message}");
            return false;
        }
    }
}
