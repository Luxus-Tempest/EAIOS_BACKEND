using EAIOS.Api.Domain.Organization;
using EAIOS.Api.Domain.Platform;
using EAIOS.Api.Domain.Connector;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.Persistence;

/// <summary>
/// Platform-level DbContext for cross-tenant data.
/// No global tenant query filters here — these entities span all tenants.
/// </summary>
public sealed class PlatformDbContext : DbContext
{
    public PlatformDbContext(DbContextOptions<PlatformDbContext> options) : base(options) { }

    // ── Platform-wide ─────────────────────────────────────────────────────────
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<FeatureFlagOverride> FeatureFlagOverrides => Set<FeatureFlagOverride>();
    public DbSet<ConnectorDefinition> ConnectorDefinitions => Set<ConnectorDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Organizations ─────────────────────────────────────────────────────
        modelBuilder.Entity<Organization>(b =>
        {
            b.ToTable("organizations", "platform");
            b.HasKey(o => o.Id);
            b.HasIndex(o => o.Slug).IsUnique();
            b.Property(o => o.Name).HasMaxLength(200).IsRequired();
            b.Property(o => o.Slug).HasMaxLength(100).IsRequired();
            b.Property(o => o.Status).HasConversion<string>();
            b.Property(o => o.AllowedIpRanges).HasColumnType("jsonb");
            b.Property(o => o.SsoConfig).HasColumnType("jsonb");
        });

        // ── Audit Events (APPEND ONLY) ────────────────────────────────────────
        modelBuilder.Entity<AuditEvent>(b =>
        {
            b.ToTable("events", "audit");
            b.HasKey(a => a.Id);
            b.HasIndex(a => new { a.OrganizationId, a.OccurredAt });
            b.HasIndex(a => new { a.OrganizationId, a.ActorId });
            b.HasIndex(a => new { a.OrganizationId, a.Action });
            b.Property(a => a.OldValuesJson).HasColumnType("jsonb");
            b.Property(a => a.NewValuesJson).HasColumnType("jsonb");
            b.Property(a => a.AdditionalDataJson).HasColumnType("jsonb");
            b.Property(a => a.Result).HasConversion<string>();
            b.Property(a => a.Id).ValueGeneratedNever();
        });

        // ── Feature Flags ─────────────────────────────────────────────────────
        modelBuilder.Entity<FeatureFlag>(b =>
        {
            b.ToTable("feature_flags", "platform");
            b.HasKey(f => f.Id);
            b.HasIndex(f => f.Key).IsUnique();
            b.Property(f => f.Type).HasConversion<string>();
            b.HasMany(f => f.Overrides).WithOne().HasForeignKey(o => o.FeatureFlagId);
        });

        modelBuilder.Entity<FeatureFlagOverride>(b =>
        {
            b.ToTable("feature_flag_overrides", "platform");
            b.HasKey(f => f.Id);
            b.HasIndex(f => new { f.FeatureFlagId, f.OrganizationId }).IsUnique();
        });

        // ── Connector Definitions ─────────────────────────────────────────────
        modelBuilder.Entity<ConnectorDefinition>(b =>
        {
            b.ToTable("connector_definitions", "platform");
            b.HasKey(c => c.Id);
            b.HasIndex(c => c.Slug).IsUnique();
            b.Property(c => c.Category).HasConversion<string>();
            b.Property(c => c.AuthType).HasConversion<string>();
            b.Property(c => c.SupportedCapabilities).HasColumnType("text[]");
            b.Property(c => c.SchemaJson).HasColumnType("jsonb");
        });
    }
}
