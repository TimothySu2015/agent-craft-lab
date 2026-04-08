using Microsoft.EntityFrameworkCore;

namespace AgentCraftLab.Search.Providers.Sqlite;

/// <summary>
/// 搜尋引擎專用 DbContext — 獨立於 Engine 的 AppDbContext，避免汙染核心資料庫。
/// </summary>
public class SearchDbContext : DbContext
{
    public SearchDbContext(DbContextOptions<SearchDbContext> options) : base(options)
    {
    }

    public DbSet<SearchChunkEntity> SearchChunks => Set<SearchChunkEntity>();
    public DbSet<SearchIndexEntity> SearchIndexes => Set<SearchIndexEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SearchIndexEntity>(entity =>
        {
            entity.HasKey(e => e.Name);
            entity.Property(e => e.Name).HasMaxLength(255);
        });

        modelBuilder.Entity<SearchChunkEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(64);
            entity.Property(e => e.IndexName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.FileName).HasMaxLength(500);
            entity.HasIndex(e => e.IndexName);
            entity.HasIndex(e => new { e.IndexName, e.Id });
        });
    }
}

/// <summary>搜尋索引元資料。</summary>
public class SearchIndexEntity
{
    public string Name { get; set; } = "";
    public long DocumentCount { get; set; }
    public long CreatedAtTicks { get; set; }
    public long? LastUpdatedAtTicks { get; set; }
}

/// <summary>搜尋文件片段（含向量 BLOB）。</summary>
public class SearchChunkEntity
{
    public string Id { get; set; } = "";
    public string IndexName { get; set; } = "";
    public string Content { get; set; } = "";
    public string FileName { get; set; } = "";
    public int ChunkIndex { get; set; }
    public byte[]? EmbeddingBlob { get; set; }
    public string? MetadataJson { get; set; }
}
