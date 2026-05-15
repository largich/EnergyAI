using EnergyAi.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnergyAi.Server.Data;

public class ChatDbContext : DbContext
{
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options) { }

    public DbSet<ChatSession>       Sessions => Set<ChatSession>();
    public DbSet<ChatMessageRecord> Messages => Set<ChatMessageRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChatSession>(e =>
        {
            e.ToTable("session", schema: "chat");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("NEWSEQUENTIALID()");
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(500);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("GETUTCDATE()");
            e.Property(x => x.IsPinned).HasColumnName("is_pinned").HasDefaultValue(false);
        });

        modelBuilder.Entity<ChatMessageRecord>(e =>
        {
            e.ToTable("message", schema: "chat");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("NEWSEQUENTIALID()");
            e.Property(x => x.SessionId).HasColumnName("session_id");
            e.Property(x => x.Role).HasColumnName("role").HasMaxLength(20);
            e.Property(x => x.Content).HasColumnName("content").HasColumnType("nvarchar(max)");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("GETUTCDATE()");

            e.HasOne(x => x.Session)
             .WithMany(s => s.Messages)
             .HasForeignKey(x => x.SessionId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.SessionId);
        });
    }
}
