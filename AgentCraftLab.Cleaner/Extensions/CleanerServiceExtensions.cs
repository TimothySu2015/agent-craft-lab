using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Partitioners;
using AgentCraftLab.Cleaner.Pipeline;
using AgentCraftLab.Cleaner.Rules;
using AgentCraftLab.Cleaner.SchemaMapper;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Cleaner.Extensions;

public static class CleanerServiceExtensions
{
    /// <summary>
    /// 註冊 CraftCleaner 完整服務（Pipeline + 所有內建 Partitioner + 清洗規則）。
    /// </summary>
    public static IServiceCollection AddCraftCleaner(this IServiceCollection services)
    {
        // Pipeline
        services.AddSingleton<IDocumentCleaner, CleaningPipeline>();

        // 內建 Partitioner（所有格式）
        services.AddSingleton<IPartitioner, DocxPartitioner>();
        services.AddSingleton<IPartitioner>(sp =>
            new PptxPartitioner(sp.GetService<IOcrProvider>(), sp.GetService<IImageDescriber>()));
        services.AddSingleton<IPartitioner, HtmlPartitioner>();
        services.AddSingleton<IPartitioner, PlainTextPartitioner>();
        services.AddSingleton<IPartitioner, XlsxPartitioner>();
        services.AddSingleton<IPartitioner>(sp =>
            new PdfPartitioner(sp.GetService<IOcrProvider>(), sp.GetService<IImageDescriber>()));
        services.AddSingleton<IPartitioner>(sp =>
            new ImagePartitioner(sp.GetService<IOcrProvider>(), sp.GetService<IImageDescriber>()));

        // 內建清洗規則
        services.AddSingleton<ICleaningRule, CleanNonAsciiRule>();
        services.AddSingleton<ICleaningRule, UnicodeNormalizeRule>();
        services.AddSingleton<ICleaningRule, CleanWhitespaceRule>();
        services.AddSingleton<ICleaningRule, CleanBulletsRule>();
        services.AddSingleton<ICleaningRule, CleanOrderedBulletsRule>();
        services.AddSingleton<ICleaningRule, CleanDashesRule>();
        services.AddSingleton<ICleaningRule, GroupBrokenParagraphsRule>();

        // 內建過濾器
        services.AddSingleton<IElementFilter, RemoveHeaderFooterFilter>();

        return services;
    }

    /// <summary>新增自訂清洗規則</summary>
    public static IServiceCollection AddCleaningRule<TRule>(this IServiceCollection services)
        where TRule : class, ICleaningRule
    {
        services.AddSingleton<ICleaningRule, TRule>();
        return services;
    }

    /// <summary>新增自訂元素過濾器</summary>
    public static IServiceCollection AddElementFilter<TFilter>(this IServiceCollection services)
        where TFilter : class, IElementFilter
    {
        services.AddSingleton<IElementFilter, TFilter>();
        return services;
    }

    /// <summary>新增自訂 Partitioner</summary>
    public static IServiceCollection AddPartitioner<TPartitioner>(this IServiceCollection services)
        where TPartitioner : class, IPartitioner
    {
        services.AddSingleton<IPartitioner, TPartitioner>();
        return services;
    }

    /// <summary>
    /// 註冊圖片描述 Provider（橋接外部多模態 LLM 到 CraftCleaner）。
    /// ImagePartitioner / PptxPartitioner / PdfPartitioner 會自動使用此 Provider。
    /// </summary>
    public static IServiceCollection AddCraftCleanerImageDescriber(
        this IServiceCollection services,
        Func<byte[], string, ImageDescriptionContext?, CancellationToken,
            Task<ImageDescriptionResult>> describeFunc)
    {
        services.AddSingleton<IImageDescriber>(new ImageDescriberAdapter(describeFunc));
        return services;
    }

    /// <summary>
    /// 註冊 OCR Provider（橋接外部 OCR 引擎到 CraftCleaner）。
    /// ImagePartitioner 會自動使用此 Provider。
    ///
    /// 用法（在 Engine 或 Api 層，有 IOcrEngine 的地方）：
    /// <code>
    /// services.AddCraftCleanerOcr((imageData, langs, ct) =>
    /// {
    ///     var engine = sp.GetRequiredService&lt;IOcrEngine&gt;();
    ///     var result = await engine.RecognizeAsync(imageData, langs, ct);
    ///     return (result.Text, result.Confidence);
    /// });
    /// </code>
    /// </summary>
    public static IServiceCollection AddCraftCleanerOcr(
        this IServiceCollection services,
        Func<byte[], IReadOnlyList<string>?, CancellationToken, Task<(string Text, float Confidence)>> recognizeFunc)
    {
        services.AddSingleton<IOcrProvider>(new OcrEngineAdapter(recognizeFunc));
        return services;
    }

    /// <summary>
    /// 註冊 Schema Mapper 服務（LLM 結構化擷取 + 模板管理）。
    /// 需要提供 ILlmProvider 和模板目錄。
    /// </summary>
    /// <param name="services">DI 容器</param>
    /// <param name="templatesDirectory">Schema 模板目錄（預設為 Data/schema-templates/）</param>
    public static IServiceCollection AddSchemaMapper(
        this IServiceCollection services,
        string? templatesDirectory = null)
    {
        var dir = templatesDirectory
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "schema-templates");

        services.AddSingleton<ISchemaTemplateProvider>(new FileSchemaTemplateProvider(dir));
        // ISchemaMapper 不註冊為 Singleton — 需要 ILlmProvider（依賴 credentials），
        // 由呼叫端（RefineryService / SchemaMapperEndpoints）按需 new LlmSchemaMapper(provider)。

        return services;
    }
}
