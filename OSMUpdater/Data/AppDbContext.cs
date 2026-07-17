using Microsoft.EntityFrameworkCore;
using OsmUpdateUtility.Models;

namespace OsmUpdateUtility.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<MapRegion> MapRegions => Set<MapRegion>();
    public DbSet<UpdateLog> UpdateLogs => Set<UpdateLog>();
    public DbSet<UpdateSettings> UpdateSettings => Set<UpdateSettings>();
    public DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MapRegion>(entity =>
        {
            entity.ToTable("MapRegions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.Name).HasColumnName("Name");
            entity.Property(e => e.Code).HasColumnName("Code");
            entity.Property(e => e.GeofabrikUrl).HasColumnName("GeofabrikUrl");
            entity.Property(e => e.StateUrl).HasColumnName("StateUrl");
            entity.Property(e => e.IsActive).HasColumnName("IsActive");
            entity.Property(e => e.AutoUpdate).HasColumnName("AutoUpdate");
            entity.Property(e => e.LastUpdateTimestamp).HasColumnName("LastUpdateTimestamp");
            entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt").HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt).HasColumnName("UpdatedAt").HasDefaultValueSql("NOW()");

            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => e.IsActive);
        });

        modelBuilder.Entity<UpdateLog>(entity =>
        {
            entity.ToTable("UpdateLogs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.RegionId).HasColumnName("RegionId");
            entity.Property(e => e.UpdateType).HasColumnName("UpdateType");
            entity.Property(e => e.Status).HasColumnName("Status");
            entity.Property(e => e.StartedAt).HasColumnName("StartedAt").HasDefaultValueSql("NOW()");
            entity.Property(e => e.FinishedAt).HasColumnName("FinishedAt");
            entity.Property(e => e.DurationSeconds).HasColumnName("DurationSeconds");
            entity.Property(e => e.LogOutput).HasColumnName("LogOutput");
            entity.Property(e => e.ErrorMessage).HasColumnName("ErrorMessage");
            entity.Property(e => e.PbfFilePath).HasColumnName("PbfFilePath");
            entity.Property(e => e.OscFilePath).HasColumnName("OscFilePath");
            entity.Property(e => e.RecordsProcessed).HasColumnName("RecordsProcessed");
            entity.Property(e => e.FromTimestamp).HasColumnName("FromTimestamp");
            entity.Property(e => e.ToTimestamp).HasColumnName("ToTimestamp");

            entity.HasIndex(e => e.RegionId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.StartedAt);
            entity.HasOne(e => e.Region)
                  .WithMany()
                  .HasForeignKey(e => e.RegionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UpdateSettings>(entity =>
        {
            entity.ToTable("update_settings");
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasColumnName("key");
            entity.Property(e => e.Value).HasColumnName("value");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.Username).HasColumnName("Username");
            entity.Property(e => e.PasswordHash).HasColumnName("PasswordHash");
            entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt").HasDefaultValueSql("NOW()");
        });
    }
}