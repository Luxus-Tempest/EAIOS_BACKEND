using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Domain.Shared.Interfaces;
using EAIOS.Api.Domain.Shared.Primitives;
using EAIOS.Api.Domain.Identity;
using EAIOS.Api.Domain.Organization;
using EAIOS.Api.Domain.AccessControl;
using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Domain.Knowledge;
using EAIOS.Api.Domain.Agent;
using EAIOS.Api.Domain.Workflow;
using EAIOS.Api.Domain.Search;
using EAIOS.Api.Domain.Analytics;
using EAIOS.Api.Domain.Notification;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Text.Json;

namespace EAIOS.Api.Infrastructure.Persistence;

/// <summary>
/// Main tenant-scoped DbContext.
/// All entities are automatically filtered by OrganizationId (Global Query Filters).
/// Soft-deleted records are excluded by default.
/// Schema-per-tenant: uses org_{id} schema in PostgreSQL.
/// </summary>
public sealed class EaiosDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    public EaiosDbContext(DbContextOptions<EaiosDbContext> options,
        ITenantContext tenantContext, ICurrentUser currentUser)
        : base(options)
    {
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    // ── Identity ──────────────────────────────────────────────────────────────
    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<MfaCredential> MfaCredentials => Set<MfaCredential>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<Invitation> Invitations => Set<Invitation>();

    // ── Organization ──────────────────────────────────────────────────────────
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Membership> Memberships => Set<Membership>();

    // ── Access Control ────────────────────────────────────────────────────────
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<ResourceAcl> ResourceAcls => Set<ResourceAcl>();

    // ── Resource ──────────────────────────────────────────────────────────────
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<MetadataTemplate> MetadataTemplates => Set<MetadataTemplate>();
    public DbSet<MetadataValue> MetadataValues => Set<MetadataValue>();
    public DbSet<DocumentShare> DocumentShares => Set<DocumentShare>();
    public DbSet<LegalHold> LegalHolds => Set<LegalHold>();

    // ── Knowledge ─────────────────────────────────────────────────────────────
    public DbSet<KnowledgeItem> KnowledgeItems => Set<KnowledgeItem>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();
    public DbSet<KnowledgeRelation> KnowledgeRelations => Set<KnowledgeRelation>();
    public DbSet<KnowledgePack> KnowledgePacks => Set<KnowledgePack>();

    // ── Agent ─────────────────────────────────────────────────────────────────
    public DbSet<Domain.Agent.Agent> Agents => Set<Domain.Agent.Agent>();
    public DbSet<AgentVersion> AgentVersions => Set<AgentVersion>();
    public DbSet<AgentExecution> AgentExecutions => Set<AgentExecution>();
    public DbSet<AgentMemory> AgentMemories => Set<AgentMemory>();
    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();

    // ── Workflow ──────────────────────────────────────────────────────────────
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowDefinitionVersion> WorkflowDefinitionVersions => Set<WorkflowDefinitionVersion>();
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<WorkflowTask> WorkflowTasks => Set<WorkflowTask>();

    // ── Search ────────────────────────────────────────────────────────────────
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<Embedding> Embeddings => Set<Embedding>();

    // ── Analytics ─────────────────────────────────────────────────────────────
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();

    // ── Notifications ─────────────────────────────────────────────────────────
    public DbSet<Domain.Notification.Notification> Notifications => Set<Domain.Notification.Notification>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();

    // ═══════════════════════════════════════════════════════════════════════════
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all IEntityTypeConfiguration<T> from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // ── Global Query Filters — Applied automatically to ALL ITenantScoped entities ──
        // This architectural constraint makes cross-tenant data leakage impossible.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (!typeof(TenantEntity).IsAssignableFrom(clrType)) continue;

            // Filter 1: Tenant isolation
            var tenantFilter = CreateTenantFilter(clrType);
            // Filter 2: Soft delete exclusion
            var softDeleteFilter = CreateSoftDeleteFilter(clrType);

            // Combine both filters
            modelBuilder.Entity(clrType).HasQueryFilter(
                CombineFilters(clrType, tenantFilter, softDeleteFilter));
        }
    }

    private static System.Linq.Expressions.LambdaExpression CreateTenantFilter(Type entityType)
    {
        // Builds: e => e.OrganizationId == _tenantContext.OrganizationId
        var parameter = System.Linq.Expressions.Expression.Parameter(entityType, "e");
        var orgIdProperty = System.Linq.Expressions.Expression.Property(parameter, nameof(TenantEntity.OrganizationId));
        // We'll use a constant check that's set per-request via the context
        return System.Linq.Expressions.Expression.Lambda(
            System.Linq.Expressions.Expression.Equal(
                orgIdProperty,
                System.Linq.Expressions.Expression.Constant(Guid.Empty) // Placeholder — overridden at query time
            ), parameter);
    }

    private static System.Linq.Expressions.LambdaExpression CreateSoftDeleteFilter(Type entityType)
    {
        var parameter = System.Linq.Expressions.Expression.Parameter(entityType, "e");
        var isDeletedProperty = System.Linq.Expressions.Expression.Property(parameter, nameof(TenantEntity.IsDeleted));
        return System.Linq.Expressions.Expression.Lambda(
            System.Linq.Expressions.Expression.Not(isDeletedProperty), parameter);
    }

    private System.Linq.Expressions.LambdaExpression CombineFilters(Type entityType,
        System.Linq.Expressions.LambdaExpression tenantFilter,
        System.Linq.Expressions.LambdaExpression softDeleteFilter)
    {
        // For simplicity with in-memory, we use a simpler approach:
        // Return lambda: e => !e.IsDeleted && e.OrganizationId == currentTenantId
        var parameter = System.Linq.Expressions.Expression.Parameter(entityType, "e");
        var isDeletedProp = System.Linq.Expressions.Expression.Property(parameter, nameof(TenantEntity.IsDeleted));
        var orgIdProp = System.Linq.Expressions.Expression.Property(parameter, nameof(TenantEntity.OrganizationId));

        // We capture _tenantContext to enable dynamic filtering
        var tenantCtx = _tenantContext;
        var orgIdExpr = System.Linq.Expressions.Expression.Property(
            System.Linq.Expressions.Expression.Constant(tenantCtx),
            nameof(ITenantContext.OrganizationId));

        var notDeleted = System.Linq.Expressions.Expression.Not(isDeletedProp);
        var tenantMatch = System.Linq.Expressions.Expression.Equal(orgIdProp, orgIdExpr);
        var combined = System.Linq.Expressions.Expression.AndAlso(notDeleted, tenantMatch);

        return System.Linq.Expressions.Expression.Lambda(combined, parameter);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SaveChanges Interception — Inject OrganizationId, Audit fields, Soft Delete
    // ═══════════════════════════════════════════════════════════════════════════

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var currentUserId = _currentUser.UserId;
        var organizationId = _tenantContext.IsResolved ? _tenantContext.OrganizationId : Guid.Empty;

        foreach (var entry in ChangeTracker.Entries<TenantEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.Id == Guid.Empty)
                        entry.Entity.GetType().GetProperty(nameof(TenantEntity.Id))!
                            .SetValue(entry.Entity, Guid.CreateVersion7());
                    // Use internal setter via reflection (private set pattern)
                    InvokeInternal(entry.Entity, "SetOrganizationId", organizationId);
                    InvokeInternal(entry.Entity, "SetCreated", currentUserId);
                    break;

                case EntityState.Modified:
                    InvokeInternal(entry.Entity, "SetUpdated", currentUserId);

                    // Intercept soft-delete: if IsDeleted was changed to true
                    if (entry.Property(nameof(TenantEntity.IsDeleted)).IsModified
                        && entry.Entity.IsDeleted
                        && entry.OriginalValues.GetValue<bool>(nameof(TenantEntity.IsDeleted)) == false)
                    {
                        InvokeInternal(entry.Entity, "SetSoftDeleted", currentUserId);
                        entry.State = EntityState.Modified;
                    }
                    break;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    private static void InvokeInternal(object entity, string methodName, params object?[] args)
    {
        var method = entity.GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        method?.Invoke(entity, args);
    }
}
