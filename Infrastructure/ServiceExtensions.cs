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

        // Data Protection alimente le chiffrement des identifiants de connecteurs.
        services.AddDataProtection();
        services.AddScoped<ICredentialProtector, CredentialProtector>();
        services.AddScoped<IPermissionService,  PermissionService>();

        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PermissionAuthorizationHandler>();

        // ── Storage & AI ────────────────────────────────────────────────────
        var storageProvider = configuration["Storage:Provider"] ?? "Local";
        if (storageProvider.Equals("MinIO", StringComparison.OrdinalIgnoreCase) ||
            storageProvider.Equals("S3", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<Amazon.S3.IAmazonS3>(sp =>
            {
                var s3Config = new Amazon.S3.AmazonS3Config
                {
                    ServiceURL = configuration["Storage:S3:ServiceUrl"] ?? "http://localhost:9000",
                    ForcePathStyle = configuration.GetValue("Storage:S3:ForcePathStyle", true),
                    UseHttp = true
                };
                var accessKey = configuration["Storage:S3:AccessKey"] ?? "minioadmin";
                var secretKey = configuration["Storage:S3:SecretKey"] ?? "minioadmin";
                return new Amazon.S3.AmazonS3Client(accessKey, secretKey, s3Config);
            });
            services.AddScoped<IStorageService, MinioStorageService>();
        }
        else
        {
            services.AddScoped<IStorageService, LocalStorageService>();
        }

        // ── Fournisseur LLM ─────────────────────────────────────────────────
        // Le stub reste le defaut : il permet de faire tourner toute la chaine IA
        // sans cle API. Ai:Provider bascule sur un vrai fournisseur.
        var aiProvider = configuration["Ai:Provider"] ?? "Stub";
        if (aiProvider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
            || aiProvider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase)
            || aiProvider.Equals("Compatible", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient("LlmClient");
            services.AddScoped<ILlmService, OpenAiLlmService>();
        }
        else
        {
            services.AddScoped<ILlmService, StubLlmService>();
        }

        // ── Recherche vectorielle ───────────────────────────────────────────
        services.AddScoped<IVectorSearchService, VectorSearchService>();

        // Le runtime d'agents (agent-runtime, Python) indexe désormais les
        // segments de connaissance dans pgvector. Deux producteurs d'embeddings
        // coexistant écriraient des vecteurs dans des formats et des dimensions
        // différents, que rien ne pourrait ensuite comparer.
        //
        // Le worker historique reste dans le dépôt et se rallume par
        // configuration : `AgentRuntime:OwnsIndexing = false` le réactive si le
        // runtime devait être retiré.
        var runtimeOwnsIndexing = configuration.GetValue("AgentRuntime:OwnsIndexing", true);
        if (!runtimeOwnsIndexing)
            services.AddHostedService<EAIOS.Api.Infrastructure.BackgroundJobs.EmbeddingWorker>();

        // ── Extraction de texte et ingestion documentaire ───────────────────
        services.AddSingleton<EAIOS.Api.Infrastructure.Extraction.ITextExtractor,
                              EAIOS.Api.Infrastructure.Extraction.TextExtractionService>();
        services.AddScoped<EAIOS.Api.Application.Knowledge.IDocumentIngestionService,
                           EAIOS.Api.Application.Knowledge.DocumentIngestionService>();
        services.AddHostedService<EAIOS.Api.Infrastructure.BackgroundJobs.DocumentIngestionWorker>();

        // ── Lecture gouvernée et rétention ──────────────────────────────────
        services.AddScoped<EAIOS.Api.Application.Resource.IDocumentAccessService,
                           EAIOS.Api.Application.Resource.DocumentAccessService>();
        services.AddHostedService<EAIOS.Api.Infrastructure.BackgroundJobs.RetentionWorker>();

        // ── Le sens du temps et de la parole ────────────────────────────────
        services.AddScoped<EAIOS.Api.Application.Notification.INotificationDispatcher,
                           EAIOS.Api.Application.Notification.NotificationDispatcher>();
        services.AddHostedService<EAIOS.Api.Infrastructure.BackgroundJobs.SchedulerWorker>();

        // ── Email ───────────────────────────────────────────────────────────
        var emailProvider = configuration["Email:Provider"] ?? "Logging";
        if (emailProvider.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
            services.AddScoped<EAIOS.Api.Infrastructure.Email.IEmailService, EAIOS.Api.Infrastructure.Email.SmtpEmailService>();
        else
            services.AddScoped<EAIOS.Api.Infrastructure.Email.IEmailService, EAIOS.Api.Infrastructure.Email.LoggingEmailService>();

        // ── Analytics tracking ──────────────────────────────────────────────
        services.AddScoped<EAIOS.Api.Infrastructure.Analytics.IAnalyticsTracker,
                           EAIOS.Api.Infrastructure.Analytics.AnalyticsTracker>();

        // ── Rapports asynchrones ────────────────────────────────────────────
        services.AddScoped<EAIOS.Api.Application.Analytics.IReportService,
                           EAIOS.Api.Application.Analytics.ReportService>();
        services.AddSingleton<EAIOS.Api.Infrastructure.BackgroundJobs.ReportQueueSignal>();
        services.AddHostedService<EAIOS.Api.Infrastructure.BackgroundJobs.ReportGenerationWorker>();
        services.AddHostedService<EAIOS.Api.Infrastructure.BackgroundJobs.ReportRetentionWorker>();

        // ── Livraison des webhooks ──────────────────────────────────────────
        services.AddSingleton<EAIOS.Api.Infrastructure.BackgroundJobs.WebhookDeliveryQueue>();
        services.AddHostedService<EAIOS.Api.Infrastructure.BackgroundJobs.WebhookDeliveryWorker>();

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
        services.AddScoped<EAIOS.Api.Application.Agent.IAgentDecisionService, EAIOS.Api.Application.Agent.AgentDecisionService>();
        services.AddScoped<EAIOS.Api.Application.Agent.IAgentEvaluationService, EAIOS.Api.Application.Agent.AgentEvaluationService>();
        // Emet la portee d'execution signee que le runtime d'agents verifie.
        services.AddScoped<EAIOS.Api.Infrastructure.Security.IAgentContextService, EAIOS.Api.Infrastructure.Security.AgentContextService>();

        // ── Runtime d'agents (agent-runtime, Python) ────────────────────────
        // Client typé : le pool de connexions est géré par la fabrique, et le
        // délai d'attente couvre une exécution complète — un agent qui appelle
        // trois outils et résume prend bien plus qu'une requête ordinaire.
        var runtimeBaseUrl = configuration["AgentRuntime:BaseUrl"] ?? "http://localhost:8080";
        var runtimeTimeout = configuration.GetValue("AgentRuntime:TimeoutSeconds", 180);

        services.AddHttpClient<EAIOS.Api.Infrastructure.AI.IAgentRuntimeClient,
                               EAIOS.Api.Infrastructure.AI.AgentRuntimeClient>(client =>
        {
            client.BaseAddress = new Uri(runtimeBaseUrl.TrimEnd('/') + "/");
            client.Timeout     = TimeSpan.FromSeconds(runtimeTimeout);
        });
        services.AddScoped<EAIOS.Api.Application.Workflow.IWorkflowService, EAIOS.Api.Application.Workflow.WorkflowService>();
        services.AddScoped<EAIOS.Api.Application.Workflow.IWorkflowAgentContinuation>(sp => (EAIOS.Api.Application.Workflow.WorkflowService)sp.GetRequiredService<EAIOS.Api.Application.Workflow.IWorkflowService>());
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
        services.AddScoped<ILegalHoldRepository,         LegalHoldRepository>();
        services.AddScoped<IMetadataValueRepository,     MetadataValueRepository>();
        services.AddScoped<IMetadataTemplateRepository,  MetadataTemplateRepository>();

        // ── Knowledge Repositories ──────────────────────────────────────────
        services.AddScoped<IKnowledgeItemRepository,     KnowledgeItemRepository>();
        services.AddScoped<IKnowledgeChunkRepository,    KnowledgeChunkRepository>();
        services.AddScoped<IKnowledgePackRepository,     KnowledgePackRepository>();
        services.AddScoped<IKnowledgeRelationRepository, KnowledgeRelationRepository>();

        // ── Agent Repositories ──────────────────────────────────────────────
        services.AddScoped<IAgentRepository,          AgentRepository>();
        services.AddScoped<IAgentExecutionRepository, AgentExecutionRepository>();
        services.AddScoped<IAgentConversationRepository, AgentConversationRepository>();
        services.AddScoped<IAgentMemoryRepository,    AgentMemoryRepository>();
        services.AddScoped<IAgentVersionRepository,   AgentVersionRepository>();

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
        services.AddScoped<IReportJobRepository,          ReportJobRepository>();

        // ── HTTP Clients ────────────────────────────────────────────────────
        services.AddHttpClient("WebhookClient");
        services.AddHttpClient("ConnectorProbe");

        return services;
    }
}
