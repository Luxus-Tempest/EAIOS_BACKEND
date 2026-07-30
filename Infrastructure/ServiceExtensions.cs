using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Infrastructure.AI;
using EAIOS.Api.Infrastructure.Audit;
using EAIOS.Api.Infrastructure.MultiTenancy;
using EAIOS.Api.Infrastructure.Notifications;
using EAIOS.Api.Infrastructure.Persistence;
using EAIOS.Api.Infrastructure.Persistence.Interceptors;
using EAIOS.Api.Infrastructure.Persistence.Repositories.AccessControl;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Agent;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Identity;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Knowledge;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Organization;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Resource;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Workflow;
using EAIOS.Api.Infrastructure.Security;
using EAIOS.Api.Infrastructure.Storage;
using EAIOS.Api.Middleware;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure;

public static class ServiceExtensions
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Multi-Tenancy ───────────────────────────────────────────────────
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ICurrentUser,   RequestContext>();

        // ── DbContexts ──────────────────────────────────────────────────────
        var useInMemory = configuration.GetValue("UseInMemoryDatabase", true);

        if (useInMemory)
        {
            services.AddDbContext<EaiosDbContext>((sp, opt) =>
                opt.UseInMemoryDatabase("EaiosDevDb")
                   .EnableSensitiveDataLogging()
                   .EnableDetailedErrors());

            services.AddDbContext<PlatformDbContext>((sp, opt) =>
                opt.UseInMemoryDatabase("PlatformDevDb"));
        }
        else
        {
            services.AddSingleton<TenantSessionInterceptor>();
            services.AddSingleton<AuditSaveChangesInterceptor>();

            services.AddDbContext<EaiosDbContext>((sp, opt) =>
            {
                opt.UseNpgsql(
                    configuration.GetConnectionString("DefaultConnection"),
                    npg => npg.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)
                              .CommandTimeout(30))
                   .AddInterceptors(
                       sp.GetRequiredService<TenantSessionInterceptor>(),
                       sp.GetRequiredService<AuditSaveChangesInterceptor>());
            });

            services.AddDbContext<PlatformDbContext>((sp, opt) =>
                opt.UseNpgsql(
                    configuration.GetConnectionString("DefaultConnection"),
                    npg => npg.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)));
        }

        // ── Security Services ───────────────────────────────────────────────
        services.AddSingleton<ITokenService,    TokenService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<ITotpService,     TotpService>();
        services.AddSingleton<IApiKeyService,   ApiKeyService>();
        services.AddScoped<IPermissionService,  PermissionService>();

        // ── Storage & AI ────────────────────────────────────────────────────
        services.AddScoped<IStorageService, LocalStorageService>();
        services.AddScoped<ILlmService,     StubLlmService>();

        // ── Domain Services ─────────────────────────────────────────────────
        services.AddScoped<IAuditService,         AuditService>();
        services.AddScoped<INotificationService,  InMemoryNotificationService>();

        // ── Identity Repositories ───────────────────────────────────────────
        services.AddScoped<IUserRepository,        UserRepository>();
        services.AddScoped<ISessionRepository,     SessionRepository>();
        services.AddScoped<IMfaCredentialRepository, MfaCredentialRepository>();
        services.AddScoped<IApiKeyRepository,      ApiKeyRepository>();
        services.AddScoped<IInvitationRepository,  InvitationRepository>();

        // ── Organization Repositories ───────────────────────────────────────
        services.AddScoped<IWorkspaceRepository,   WorkspaceRepository>();
        services.AddScoped<IDepartmentRepository,  DepartmentRepository>();
        services.AddScoped<IMembershipRepository,  MembershipRepository>();

        // ── Access Control Repositories ─────────────────────────────────────
        services.AddScoped<IRoleRepository,        RoleRepository>();
        services.AddScoped<IPermissionRepository,  PermissionRepository>();
        services.AddScoped<IUserRoleRepository,    UserRoleRepository>();
        services.AddScoped<IPolicyRepository,      PolicyRepository>();
        services.AddScoped<IResourceAclRepository, ResourceAclRepository>();

        // ── Resource Repositories ───────────────────────────────────────────
        services.AddScoped<IDocumentRepository,         DocumentRepository>();
        services.AddScoped<IDocumentVersionRepository,  DocumentVersionRepository>();
        services.AddScoped<IFolderRepository,           FolderRepository>();
        services.AddScoped<IDocumentShareRepository,    DocumentShareRepository>();
        services.AddScoped<ILegalHoldRepository,        LegalHoldRepository>();

        // ── Knowledge Repositories ──────────────────────────────────────────
        services.AddScoped<IKnowledgeItemRepository,  KnowledgeItemRepository>();
        services.AddScoped<IKnowledgeChunkRepository, KnowledgeChunkRepository>();
        services.AddScoped<IKnowledgePackRepository,  KnowledgePackRepository>();

        // ── Agent Repositories ──────────────────────────────────────────────
        services.AddScoped<IAgentRepository,          AgentRepository>();
        services.AddScoped<IAgentExecutionRepository, AgentExecutionRepository>();
        services.AddScoped<IAgentMemoryRepository,    AgentMemoryRepository>();

        // ── Workflow Repositories ───────────────────────────────────────────
        services.AddScoped<IWorkflowDefinitionRepository, WorkflowDefinitionRepository>();
        services.AddScoped<IWorkflowInstanceRepository,   WorkflowInstanceRepository>();
        services.AddScoped<IWorkflowTaskRepository,       WorkflowTaskRepository>();

        // ── Misc Repositories ───────────────────────────────────────────────
        services.AddScoped<ISavedSearchRepository,      SavedSearchRepository>();
        services.AddScoped<INotificationRepository,     NotificationRepository>();
        services.AddScoped<IAnalyticsEventRepository,   AnalyticsEventRepository>();
        services.AddScoped<IConnectorInstanceRepository, ConnectorInstanceRepository>();
        services.AddScoped<ISyncJobRepository,           SyncJobRepository>();

        return services;
    }
}
