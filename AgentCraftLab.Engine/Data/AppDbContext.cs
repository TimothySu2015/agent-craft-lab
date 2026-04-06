using Microsoft.EntityFrameworkCore;

namespace AgentCraftLab.Engine.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<WorkflowDocument> Workflows => Set<WorkflowDocument>();
    public DbSet<CredentialDocument> Credentials => Set<CredentialDocument>();
    public DbSet<RequestLogDocument> RequestLogs => Set<RequestLogDocument>();
    public DbSet<SkillDocument> Skills => Set<SkillDocument>();
    public DbSet<TemplateDocument> Templates => Set<TemplateDocument>();
    public DbSet<ScheduleDocument> Schedules => Set<ScheduleDocument>();
    public DbSet<ScheduleLogDocument> ScheduleLogs => Set<ScheduleLogDocument>();
    public DbSet<ExecutionMemoryDocument> ExecutionMemories => Set<ExecutionMemoryDocument>();
    public DbSet<DataSourceDocument> DataSources => Set<DataSourceDocument>();
    public DbSet<KnowledgeBaseDocument> KnowledgeBases => Set<KnowledgeBaseDocument>();
    public DbSet<KbFileDocument> KbFiles => Set<KbFileDocument>();
    public DbSet<ApiKeyDocument> ApiKeys => Set<ApiKeyDocument>();
    public DbSet<RefineryProject> RefineryProjects => Set<RefineryProject>();
    public DbSet<RefineryFile> RefineryFiles => Set<RefineryFile>();
    public DbSet<RefineryOutput> RefineryOutputs => Set<RefineryOutput>();
    public DbSet<CraftMdDocument> CraftMds => Set<CraftMdDocument>();
    public DbSet<CheckpointDocument> Checkpoints => Set<CheckpointDocument>();
    public DbSet<EntityMemoryDocument> EntityMemories => Set<EntityMemoryDocument>();
    public DbSet<ContextualMemoryDocument> ContextualMemories => Set<ContextualMemoryDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkflowDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(50);
            entity.Property(e => e.WorkflowJson).IsRequired();
        });

        modelBuilder.Entity<CredentialDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Provider).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.EncryptedApiKey).IsRequired();
        });

        modelBuilder.Entity<SkillDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Category).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Instructions).IsRequired();
        });

        modelBuilder.Entity<TemplateDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Category).IsRequired().HasMaxLength(50);
            entity.Property(e => e.WorkflowJson).IsRequired();
            entity.HasIndex(e => e.UserId);
        });

        modelBuilder.Entity<RequestLogDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.WorkflowKey).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Protocol).IsRequired().HasMaxLength(10);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.WorkflowKey);
        });

        modelBuilder.Entity<ScheduleDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.WorkflowId).IsRequired();
            entity.Property(e => e.CronExpression).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.Enabled, e.NextRunAt });
        });

        modelBuilder.Entity<ScheduleLogDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ScheduleId).IsRequired();
            entity.HasIndex(e => new { e.ScheduleId, e.CreatedAt });
            entity.Ignore(e => e.StatusText);
        });

        modelBuilder.Entity<ExecutionMemoryDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.GoalKeywords).IsRequired();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<CraftMdDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.HasIndex(e => new { e.UserId, e.WorkflowId });
        });

        modelBuilder.Entity<CheckpointDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExecutionId).IsRequired();
            entity.HasIndex(e => e.ExecutionId);
            entity.HasIndex(e => new { e.ExecutionId, e.Iteration });
        });

        modelBuilder.Entity<DataSourceDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Provider).IsRequired().HasMaxLength(50);
            entity.Property(e => e.UserId).IsRequired();
            entity.HasIndex(e => e.UserId);
        });

        modelBuilder.Entity<KnowledgeBaseDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.UserId).IsRequired();
            entity.HasIndex(e => e.UserId);
        });

        modelBuilder.Entity<KbFileDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.KnowledgeBaseId).IsRequired();
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(500);
            entity.HasIndex(e => e.KnowledgeBaseId);
        });

        modelBuilder.Entity<ApiKeyDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.KeyHash).IsRequired().HasMaxLength(64);
            entity.HasIndex(e => e.KeyHash);
            entity.HasIndex(e => e.UserId);
        });

        modelBuilder.Entity<RefineryProject>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.UserId).IsRequired();
            entity.HasIndex(e => e.UserId);
        });

        modelBuilder.Entity<RefineryFile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RefineryProjectId).IsRequired();
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(500);
            entity.HasIndex(e => e.RefineryProjectId);
        });

        modelBuilder.Entity<RefineryOutput>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RefineryProjectId).IsRequired();
            entity.HasIndex(e => new { e.RefineryProjectId, e.Version });
        });

        modelBuilder.Entity<EntityMemoryDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.EntityName).IsRequired();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.EntityName });
        });

        modelBuilder.Entity<ContextualMemoryDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.HasIndex(e => e.UserId);
        });
    }
}
