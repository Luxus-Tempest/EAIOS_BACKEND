using EAIOS.Api.Application.Common.Interfaces;
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
using EAIOS.Api.Domain.Webhook;
using EAIOS.Api.Domain.Connector;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace EAIOS.Api.Infrastructure.Persistence;

/// <summary>
/// Tenant-scoped DbContext. Global Query Filters assurent l'isolation par OrganizationId
/// et l'exclusion automatique des enregistrements soft-deleted.
/// </summary>
public sealed class EaiosDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    public EaiosDbContext(
        DbContextOptions<EaiosDbContext> options,
        ITenantContext tenantContext,
        ICurrentUser currentUser) : base(options)
    {
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    // ── Identity ───────────────────────────────────────────────────────────────
    public DbSet<User>             Users             => Set<User>();
    public DbSet<Session>          Sessions          => Set<Session>();
    public DbSet<MfaCredential>    MfaCredentials    => Set<MfaCredential>();
    public DbSet<ApiKey>           ApiKeys           => Set<ApiKey>();
    public DbSet<Invitation>       Invitations       => Set<Invitation>();

    // ── Organization ──────────────────────────────────────────────────────────
    public DbSet<Workspace>        Workspaces        => Set<Workspace>();
    public DbSet<Department>       Departments       => Set<Department>();
    public DbSet<Membership>       Memberships       => Set<Membership>();

    // ── Access Control ────────────────────────────────────────────────────────
    public DbSet<Role>             Roles             => Set<Role>();
    public DbSet<Permission>       Permissions       => Set<Permission>();
    public DbSet<UserRole>         UserRoles         => Set<UserRole>();
    public DbSet<Policy>           Policies          => Set<Policy>();
    public DbSet<ResourceAcl>      ResourceAcls      => Set<ResourceAcl>();

    // ── Resource ──────────────────────────────────────────────────────────────
    public DbSet<Document>         Documents         => Set<Document>();
    public DbSet<DocumentVersion>  DocumentVersions  => Set<DocumentVersion>();
    public DbSet<Folder>           Folders           => Set<Folder>();
    public DbSet<MetadataTemplate> MetadataTemplates => Set<MetadataTemplate>();
    public DbSet<MetadataValue>    MetadataValues    => Set<MetadataValue>();
    public DbSet<DocumentShare>    DocumentShares    => Set<DocumentShare>();
    public DbSet<LegalHold>        LegalHolds        => Set<LegalHold>();

    // ── Knowledge ─────────────────────────────────────────────────────────────
    public DbSet<KnowledgeItem>     KnowledgeItems     => Set<KnowledgeItem>();
    public DbSet<KnowledgeChunk>    KnowledgeChunks    => Set<KnowledgeChunk>();
    public DbSet<KnowledgeRelation> KnowledgeRelations => Set<KnowledgeRelation>();
    public DbSet<KnowledgePack>     KnowledgePacks     => Set<KnowledgePack>();

    // ── Agent ─────────────────────────────────────────────────────────────────
    public DbSet<Domain.Agent.Agent> Agents          => Set<Domain.Agent.Agent>();
    public DbSet<AgentVersion>       AgentVersions   => Set<AgentVersion>();
    public DbSet<AgentExecution>     AgentExecutions => Set<AgentExecution>();
    public DbSet<AgentMemory>        AgentMemories   => Set<AgentMemory>();
    public DbSet<PromptTemplate>     PromptTemplates => Set<PromptTemplate>();

    // ── Workflow ──────────────────────────────────────────────────────────────
    public DbSet<WorkflowDefinition>        WorkflowDefinitions        => Set<WorkflowDefinition>();
    public DbSet<WorkflowDefinitionVersion> WorkflowDefinitionVersions => Set<WorkflowDefinitionVersion>();
    public DbSet<WorkflowInstance>          WorkflowInstances          => Set<WorkflowInstance>();
    public DbSet<WorkflowTask>              WorkflowTasks              => Set<WorkflowTask>();

    // ── Search ────────────────────────────────────────────────────────────────
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<Embedding>   Embeddings    => Set<Embedding>();

    // ── Analytics ─────────────────────────────────────────────────────────────
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();

    // ── Notification ──────────────────────────────────────────────────────────
    public DbSet<Domain.Notification.Notification> Notifications       => Set<Domain.Notification.Notification>();
    public DbSet<NotificationTemplate>             NotificationTemplates => Set<NotificationTemplate>();

    // ── Webhooks ────────────────────────────
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();

    // ── Connector ───────────────────────────
    public DbSet<ConnectorDefinition> ConnectorDefinitions => Set<ConnectorDefinition>();
    public DbSet<ConnectorInstance> ConnectorInstances => Set<ConnectorInstance>();
    public DbSet<SyncJob> SyncJobs => Set<SyncJob>();

    // ═══════════════════════════════════════════════════════════════════════════
    // MODEL CREATION
    // ═══════════════════════════════════════════════════════════════════════════

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Applique toutes les IEntityTypeConfiguration<T> de l'assembly
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Global Query Filters : isolement tenant + soft-delete
        // Chaque type concret héritant de TenantEntity reçoit les deux filtres combinés
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (clrType.IsAbstract || !typeof(TenantEntity).IsAssignableFrom(clrType))
                continue;

            var parameter = System.Linq.Expressions.Expression.Parameter(clrType, "e");
            var isDeletedProp = System.Linq.Expressions.Expression.Property(parameter, nameof(TenantEntity.IsDeleted));
            var orgIdProp     = System.Linq.Expressions.Expression.Property(parameter, nameof(TenantEntity.OrganizationId));

            // Capture the context via closure so the filter is evaluated per-request
            var ctx = _tenantContext;
            var orgIdValue = System.Linq.Expressions.Expression.Property(
                System.Linq.Expressions.Expression.Constant(ctx),
                nameof(ITenantContext.OrganizationId));

            var notDeleted  = System.Linq.Expressions.Expression.Not(isDeletedProp);
            var tenantMatch = System.Linq.Expressions.Expression.Equal(orgIdProp, orgIdValue);
            var combined    = System.Linq.Expressions.Expression.AndAlso(notDeleted, tenantMatch);
            var lambda      = System.Linq.Expressions.Expression.Lambda(combined, parameter);

            modelBuilder.Entity(clrType).HasQueryFilter(lambda);

            // Concurrency Token
            modelBuilder.Entity(clrType).Property(nameof(TenantEntity.Version)).IsRowVersion();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SAVE CHANGES — Hydrate automatic audit fields
    // ═══════════════════════════════════════════════════════════════════════════

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var actorId  = _currentUser.UserId;
        var orgId    = _tenantContext.IsResolved ? _tenantContext.OrganizationId : Guid.Empty;

        foreach (var entry in ChangeTracker.Entries<TenantEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.Id == Guid.Empty)
                        SetProperty(entry.Entity, nameof(TenantEntity.Id), Guid.CreateVersion7());
                    InvokePrivate(entry.Entity, "SetOrganizationId", orgId);
                    InvokePrivate(entry.Entity, "SetCreated", actorId);
                    break;

                case EntityState.Modified:
                    InvokePrivate(entry.Entity, "SetUpdated", actorId);
                    // Soft-delete interception : si IsDeleted passe à true, on enrichit les champs
                    if (entry.Property(nameof(TenantEntity.IsDeleted)).IsModified
                        && entry.Entity.IsDeleted
                        && !entry.OriginalValues.GetValue<bool>(nameof(TenantEntity.IsDeleted)))
                    {
                        InvokePrivate(entry.Entity, "SetSoftDeleted", actorId);
                    }
                    break;
            }
        }

        return await base.SaveChangesAsync(ct);
    }

    private static void SetProperty(object entity, string name, object value)
    {
        entity.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.SetValue(entity, value);
    }

    private static void InvokePrivate(object entity, string method, params object?[] args)
    {
        entity.GetType()
              .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
              ?.Invoke(entity, args);
    }
}
