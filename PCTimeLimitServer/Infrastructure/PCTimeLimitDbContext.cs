using Microsoft.EntityFrameworkCore;
using PCTimeLimitServer.Domain.Entities;

namespace PCTimeLimitServer.Infrastructure;

public sealed class PCTimeLimitDbContext(DbContextOptions<PCTimeLimitDbContext> options) : DbContext(options)
{
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<Computer> Computers => Set<Computer>();
    public DbSet<ComputerAllowedUsageRange> ComputerAllowedUsageRanges => Set<ComputerAllowedUsageRange>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<DeviceCredential> DeviceCredentials => Set<DeviceCredential>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Username).HasMaxLength(100).IsRequired();
            entity.Property(x => x.NormalizedUsername).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(x => x.AdminCode).HasMaxLength(6).IsRequired();
            entity.HasIndex(x => x.NormalizedUsername).IsUnique();
            entity.HasIndex(x => x.AdminCode).IsUnique();
        });

        modelBuilder.Entity<Computer>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ExternalId).HasMaxLength(256).IsRequired();
            entity.Property(x => x.ComputerName).HasMaxLength(256).IsRequired();
            entity.Property(x => x.AllowedUsageJson).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.AllowedUsageUpdatedAtUtc).IsRequired();
            entity.HasIndex(x => x.ExternalId).IsUnique();
            entity.HasOne(x => x.AdminUser)
                .WithMany(x => x.Computers)
                .HasForeignKey(x => x.AdminUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.AllowedUsageRanges)
                .WithOne(x => x.Computer)
                .HasForeignKey(x => x.ComputerId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.DeviceCredential)
                .WithOne(x => x.Computer)
                .HasForeignKey<DeviceCredential>(x => x.ComputerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ComputerAllowedUsageRange>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DayOfWeek).IsRequired();
            entity.Property(x => x.StartMinute).IsRequired();
            entity.Property(x => x.EndMinute).IsRequired();
            entity.Property(x => x.Order).IsRequired();
            entity.HasIndex(x => new { x.ComputerId, x.DayOfWeek, x.StartMinute });
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasOne(x => x.AdminUser)
                .WithMany(x => x.RefreshTokens)
                .HasForeignKey(x => x.AdminUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeviceCredential>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
        });
    }
}
