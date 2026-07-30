using EAIOS.Api.Domain.Organization;
using EAIOS.Api.Domain.Platform;
using EAIOS.Api.Domain.Connector;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.Persistence;

/// <summary>
/// DbContext cross-tenant pour les données de plateforme.
/// Aucun Global Query Filter ici — ces tables couvrent TOUS les tenants.
/// </summary>
public sealed class PlatformDbContext : DbContext
{
    public PlatformDbContext(DbContextOptions<PlatformDbContext> options) : base(options) { }

    public DbSet<Organization>          Organizations        => Set<Organization>();
    public DbSet<AuditEvent>            AuditEvents          => Set<AuditEvent>();
    public DbSet<FeatureFlag>           FeatureFlags         => Set<FeatureFlag>();
    public DbSet<FeatureFlagOverride>   FeatureFlagOverrides => Set<FeatureFlagOverride>();
    public DbSet<ConnectorDefinition>   ConnectorDefinitions => Set<ConnectorDefinition>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        // ── Organizations ─────────────────────────────────────────────────────
        mb.Entity<Organization>(b =>
        {
            b.ToTable("organizations", "platform");
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).ValueGeneratedNever();
            b.HasIndex(o => o.Slug).IsUnique();
            b.Property(o => o.Name).HasMaxLength(200).IsRequired();
            b.Property(o => o.Slug).HasMaxLength(100).IsRequired();
            b.Property(o => o.Status).HasConversion<string>().HasMaxLength(30);
        });

        // ── Audit Events (APPEND-ONLY - jamais de UPDATE ou DELETE) ──────────
        mb.Entity<AuditEvent>(b =>
        {
            b.ToTable("events", "audit");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).ValueGeneratedNever();
            b.HasIndex(a => new { a.OrganizationId, a.OccurredAt });
            b.HasIndex(a => new { a.OrganizationId, a.ActorId });
            b.HasIndex(a => new { a.OrganizationId, a.Action });
            b.Property(a => a.Result).HasConversion<string>().HasMaxLength(30);
            b.Property(a => a.Action).HasMaxLength(200).IsRequired();
            b.Property(a => a.ActorType).HasMaxLength(30).IsRequired();
        });

        // ── Feature Flags ─────────────────────────────────────────────────────
        mb.Entity<FeatureFlag>(b =>
        {
            b.ToTable("feature_flags", "platform");
            b.HasKey(f => f.Id);
            b.Property(f => f.Id).ValueGeneratedNever();
            b.HasIndex(f => f.Key).IsUnique();
            b.Property(f => f.Key).HasMaxLength(200).IsRequired();
            b.Property(f => f.Type).HasConversion<string>().HasMaxLength(20);
            b.HasMany(f => f.Overrides).WithOne().HasForeignKey(o => o.FeatureFlagId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<FeatureFlagOverride>(b =>
        {
            b.ToTable("feature_flag_overrides", "platform");
            b.HasKey(f => f.Id);
            b.Property(f => f.Id).ValueGeneratedNever();
            b.HasIndex(f => new { f.FeatureFlagId, f.OrganizationId }).IsUnique();
        });

        // ── Connector Definitions ─────────────────────────────────────────────
        mb.Entity<ConnectorDefinition>(b =>
        {
            b.ToTable("connector_definitions", "platform");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).ValueGeneratedNever();
            b.HasIndex(c => c.Slug).IsUnique();
            b.Property(c => c.Category).HasConversion<string>().HasMaxLength(50);
            b.Property(c => c.AuthType).HasConversion<string>().HasMaxLength(50);
            b.Property(c => c.Name).HasMaxLength(200).IsRequired();
            b.Property(c => c.Slug).HasMaxLength(100).IsRequired();
        });
    }
}
