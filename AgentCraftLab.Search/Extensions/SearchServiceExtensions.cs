using AgentCraftLab.Search.Abstractions;
using AgentCraftLab.Search.Chunking;
using AgentCraftLab.Search.Extraction;
using AgentCraftLab.Search.Providers.InMemory;
using AgentCraftLab.Search.Providers.Sqlite;
using AgentCraftLab.Search.Reranking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentCraftLab.Search.Extensions;

/// <summary>分塊策略類型。</summary>
public enum ChunkerType
{
    /// <summary>固定大小分塊（預設，最穩定）。</summary>
    FixedSize,

    /// <summary>結構感知分塊（按 heading / 段落邊界切割）。</summary>
    Structural
}

/// <summary>
/// CraftSearch 搜尋引擎 DI 註冊擴充方法。
/// </summary>
public static class SearchServiceExtensions
{
    /// <summary>
    /// 註冊 CraftSearch 核心服務（擷取器 + 分塊器），不含搜尋引擎 Provider。
    /// 需另外呼叫 AddCraftSearchSqlite() 或 AddCraftSearchInMemory() 註冊 ISearchEngine。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <param name="chunkerType">分塊策略（預設 FixedSize）。</param>
    public static IServiceCollection AddCraftSearch(
        this IServiceCollection services,
        ChunkerType chunkerType = ChunkerType.FixedSize)
    {
        // 文字擷取器（可疊加註冊）
        services.AddSingleton<IDocumentExtractor, PdfExtractor>();
        services.AddSingleton<IDocumentExtractor, DocxExtractor>();
        services.AddSingleton<IDocumentExtractor, PptxExtractor>();
        services.AddSingleton<IDocumentExtractor, HtmlExtractor>();
        services.AddSingleton<IDocumentExtractor, PlainTextExtractor>();
        services.AddSingleton<DocumentExtractorFactory>();

        // 分塊器
        switch (chunkerType)
        {
            case ChunkerType.Structural:
                services.AddSingleton<ITextChunker, StructuralChunker>();
                break;
            default:
                services.AddSingleton<ITextChunker, FixedSizeChunker>();
                break;
        }

        // 重排序（預設不做任何事，可用 AddReranker<T>() 替換）
        services.TryAddSingleton<IReranker, NoOpReranker>();

        return services;
    }

    /// <summary>
    /// 替換預設的 NoOpReranker，註冊自訂重排序實作。
    /// </summary>
    public static IServiceCollection AddReranker<TReranker>(this IServiceCollection services)
        where TReranker : class, IReranker
    {
        // 移除既有註冊再加入新的
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IReranker));
        if (descriptor is not null)
        {
            services.Remove(descriptor);
        }

        services.AddSingleton<IReranker, TReranker>();
        return services;
    }

    /// <summary>
    /// 註冊 SQLite 搜尋引擎（持久化，支援 FTS5 + 向量 + RRF 混合搜尋）。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <param name="dbPath">SQLite 資料庫檔案路徑（預設 craftsearch.db）。</param>
    /// <param name="configureOptions">搜尋引擎選項設定。</param>
    public static IServiceCollection AddCraftSearchSqlite(
        this IServiceCollection services,
        string dbPath = "craftsearch.db",
        Action<SearchEngineOptions>? configureOptions = null)
    {
        var options = new SearchEngineOptions();
        configureOptions?.Invoke(options);
        services.AddSingleton(options);

        services.AddDbContext<SearchDbContext>(opt =>
            opt.UseSqlite($"Data Source={dbPath};Cache=Shared"));

        services.AddSingleton<ISearchEngine, SqliteSearchEngine>();

        return services;
    }

    /// <summary>
    /// 註冊記憶體搜尋引擎（測試用 / 向下相容）。
    /// </summary>
    public static IServiceCollection AddCraftSearchInMemory(
        this IServiceCollection services,
        Action<SearchEngineOptions>? configureOptions = null)
    {
        var options = new SearchEngineOptions();
        configureOptions?.Invoke(options);
        services.AddSingleton(options);

        services.AddSingleton<ISearchEngine, InMemorySearchEngine>();

        return services;
    }

    /// <summary>
    /// 初始化搜尋引擎資料庫（建立表格 + FTS5 虛擬表 + 清理過期索引）。
    /// 應在應用程式啟動時呼叫。
    /// </summary>
    public static async Task InitializeSearchDatabaseAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SearchDbContext>();

        await db.Database.EnsureCreatedAsync();

        // 使用 DELETE 日誌模式（搜尋引擎是 Singleton 單寫入者，不需要 WAL 並行讀寫）
        // WAL 模式在 FTS5 external content 操作中可能導致 shadow table 不一致
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=DELETE");
        await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL");

        // 建立 FTS5 虛擬表（獨立模式，自行管理 content 副本）
        // 不使用 external content（content=Table）避免 rowid 不一致導致 "database disk image is malformed"
        // trigram 以字元 n-gram 為基礎，不需要斷詞器即可處理 CJK 中日韓文字
        await db.Database.ExecuteSqlRawAsync("""
            CREATE VIRTUAL TABLE IF NOT EXISTS SearchChunksFts
            USING fts5(ChunkId UNINDEXED, IndexName UNINDEXED, Content, tokenize='trigram')
            """);

        // 啟動時自動清理過期索引（Singleton 從 root provider 取）
        var options = serviceProvider.GetRequiredService<SearchEngineOptions>();
        if (options.IndexTtl is not null)
        {
            var searchEngine = serviceProvider.GetRequiredService<ISearchEngine>();
            await searchEngine.CleanupStaleIndexesAsync(options.IndexTtl.Value);
        }
    }
}
