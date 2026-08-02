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
            services.AddScoped<TenantSessionInterceptor>();
            services.AddScoped<AuditSaveChangesInterceptor>();

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

        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PermissionAuthorizationHandler>();

        // ── Storage & AI ────────────────────────────────────────────────────
        services.AddScoped<IStorageService, LocalStorageService>();
        services.AddScoped<ILlmService,     StubLlmService>();

        // ── Domain & Application Services ───────────────────────────────────
        services.AddScoped<IAuditService,         AuditService>();
        services.AddScoped<INotificationService,  InMemoryNotificationService>();
        services.AddScoped<EAIOS.Api.Application.Identity.IUserService, EAIOS.Api.Application.Identity.UserService>();
        services.AddScoped<EAIOS.Api.Application.Organization.IWorkspaceService, EAIOS.Api.Application.Organization.WorkspaceService>();
        services.AddScoped<EAIOS.Api.Application.Organization.IDepartmentService, EAIOS.Api.Application.Organization.DepartmentService>();
        services.AddScoped<EAIOS.Api.Application.AccessControl.IAccessControlService, EAIOS.Api.Application.AccessControl.AccessControlService>();
        services.AddScoped<EAIOS.Api.Application.Resource.IDocumentService, EAIOS.Api.Application.Resource.DocumentService>();
        services.AddScoped<EAIOS.Api.Application.Resource.IFolderService, EAIOS.Api.Application.Resource.FolderService>();
        services.AddScoped<EAIOS.Api.Application.Knowledge.IKnowledgeService, EAIOS.Api.Application.Knowledge.KnowledgeService>();
        services.AddScoped<EAIOS.Api.Application.Knowledge.IKnowledgeGraphService, EAIOS.Api.Application.Knowledge.KnowledgeGraphService>();
        services.AddScoped<EAIOS.Api.Application.Agent.IAgentService, EAIOS.Api.Application.Agent.AgentService>();
        services.AddScoped<EAIOS.Api.Application.Agent.IAgentExecutionService, EAIOS.Api.Application.Agent.AgentExecutionService>();
        services.AddScoped<EAIOS.Api.Application.Workflow.IWorkflowService, EAIOS.Api.Application.Workflow.WorkflowService>();
        services.AddScoped<EAIOS.Api.Application.Search.ISearchService, EAIOS.Api.Application.Search.SearchService>();
        services.AddScoped<EAIOS.Api.Application.Connector.IConnectorService, EAIOS.Api.Application.Connector.ConnectorService>();
        services.AddScoped<EAIOS.Api.Application.Connector.IConnectorCatalogService, EAIOS.Api.Application.Connector.ConnectorCatalogService>();
        services.AddScoped<EAIOS.Api.Application.Notification.INotificationService, EAIOS.Api.Application.Notification.NotificationService>();
        services.AddScoped<EAIOS.Api.Application.Notification.INotificationTemplateService, EAIOS.Api.Application.Notification.NotificationTemplateService>();
        services.AddScoped<EAIOS.Api.Application.Analytics.IAnalyticsQueryService, EAIOS.Api.Application.Analytics.AnalyticsQueryService>();
        services.AddScoped<EAIOS.Api.Application.Platform.IPlatformAdminService, EAIOS.Api.Application.Platform.PlatformAdminService>();
        services.AddScoped<EAIOS.Api.Application.Webhook.IWebhookService, EAIOS.Api.Application.Webhook.WebhookService>();
        
        services.AddSingleton<EAIOS.Api.Application.Realtime.IRealtimeEventService, EAIOS.Api.Application.Realtime.RealtimeEventService>();

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
        services.AddScoped<INotificationTemplateRepository, NotificationTemplateRepository>();
        services.AddScoped<IAnalyticsEventRepository,   AnalyticsEventRepository>();
        services.AddScoped<IConnectorInstanceRepository, ConnectorInstanceRepository>();
        services.AddScoped<IConnectorDefinitionRepository, ConnectorDefinitionRepository>();
        services.AddScoped<ISyncJobRepository,           SyncJobRepository>();
        services.AddScoped<IWebhookSubscriptionRepository, WebhookSubscriptionRepository>();

        // ── HTTP Clients ────────────────────────────────────────────────────
        services.AddHttpClient("WebhookClient");

        return services;
    }
}
