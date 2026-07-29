# EAIOS API — Plan d'Implémentation Exhaustif
## Senior Engineering · Red Hat / Microsoft Style · .NET 10 · ASP.NET Core · EF Core · PostgreSQL

---

## Contexte & Objectif

**Projet existant :** Single-project ASP.NET Core avec in-memory store (dev adapter), 5 controllers partiels.

**Objectif :** Implémenter l'intégralité de l'architecture EAIOS dans ce projet unique en respectant la philosophie **feature-folder Clean Architecture**, prête à être extraite en microservices. Tout ce qui suit les guides de spécification : Entities, DTOs, Repositories, Services, Middleware, DbContext EF Core, Multi-Tenancy complet, et tous les 12 modules API.

---

## Architecture Cible — Structure Finale du Répertoire `backend/`

```
backend/
│
├── Domain/                                   ← Couche Domain (Entités, Enums, Events, Interfaces)
│   ├── Shared/
│   │   ├── Primitives/
│   │   │   ├── Entity.cs                     ← Entity<TId> base class
│   │   │   ├── TenantEntity.cs               ← Classe de base OBLIGATOIRE (OrganizationId, Audit, SoftDelete)
│   │   │   └── ValueObject.cs                ← Base record ValueObject
│   │   ├── Interfaces/
│   │   │   ├── ITenantScoped.cs              ← Contrat multi-tenant
│   │   │   ├── ISoftDeletable.cs             ← Soft delete contract
│   │   │   ├── IAuditable.cs                 ← Audit fields contract
│   │   │   └── IDomainEvent.cs               ← Marker interface events
│   │   └── Events/
│   │       └── DomainEvents.cs               ← Tous les domain events catalogués
│   │
│   ├── Identity/
│   │   └── Entities.cs                       ← User, Session, MfaCredential, ApiKey, Invitation
│   ├── Organization/
│   │   └── Entities.cs                       ← Organization, Workspace, Department, Team, Membership
│   ├── AccessControl/
│   │   └── Entities.cs                       ← Role, Permission, UserRole, Policy, ResourceAcl
│   ├── Resource/
│   │   └── Entities.cs                       ← Resource, Document, DocumentVersion, Folder,
│   │                                            MetadataTemplate, MetadataValue, ResourceShare,
│   │                                            ResourceTag, LegalHold, UploadSession
│   ├── Knowledge/
│   │   └── Entities.cs                       ← KnowledgeItem, KnowledgeChunk, KnowledgeRelation, KnowledgePack
│   ├── Agent/
│   │   └── Entities.cs                       ← Agent, AgentVersion, AgentExecution, AgentMemory,
│   │                                            AgentTool, PromptTemplate, AgentLlmConfig (VO)
│   ├── Workflow/
│   │   └── Entities.cs                       ← WorkflowDefinition, WorkflowDefinitionVersion,
│   │                                            WorkflowInstance, WorkflowTask, WorkflowStepExecution
│   ├── Search/
│   │   └── Entities.cs                       ← SavedSearch, Embedding
│   ├── Connector/
│   │   └── Entities.cs                       ← ConnectorDefinition, ConnectorInstance, SyncJob, SyncExecution
│   ├── Analytics/
│   │   └── Entities.cs                       ← AnalyticsEvent
│   ├── Notification/
│   │   └── Entities.cs                       ← Notification, NotificationTemplate
│   └── Platform/
│       └── Entities.cs                       ← AuditEvent, FeatureFlag, FeatureFlagOverride
│
├── Application/                              ← Couche Application (DTOs, Services Interfaces, Contracts)
│   ├── Common/
│   │   ├── Models/
│   │   │   ├── PagedResult.cs                ← Wrapper pagination générique
│   │   │   ├── ApiResponse.cs                ← Wrapper { data: T } conforme spec
│   │   │   └── PermissionCheckResult.cs      ← Résultat évaluation RBAC+ABAC
│   │   └── Interfaces/
│   │       ├── ICurrentUser.cs               ← Contrat résolution user courant
│   │       ├── ITenantContext.cs             ← Contrat résolution tenant courant
│   │       ├── IAuditService.cs              ← Contrat audit service
│   │       ├── INotificationService.cs       ← Contrat notification
│   │       ├── IStorageService.cs            ← Contrat stockage fichiers
│   │       └── IPermissionService.cs         ← Contrat évaluation permissions
│   │
│   ├── Identity/
│   │   └── Dtos.cs                           ← LoginRequest/Response, RegisterRequest, MfaSetupDto,
│   │                                            ApiKeyDto, UserDto, SessionDto, InvitationDto
│   ├── Organization/
│   │   └── Dtos.cs                           ← OrgDto, WorkspaceDto, DepartmentDto, MemberDto, InvitationRequest
│   ├── AccessControl/
│   │   └── Dtos.cs                           ← RoleDto, PermissionDto, PolicyDto, AclDto, AccessCheckRequest
│   ├── Resource/
│   │   └── Dtos.cs                           ← ResourceDto, DocumentDto, FolderDto, UploadRequest,
│   │                                            VersionDto, MetadataDto, ShareDto, LegalHoldDto
│   ├── Knowledge/
│   │   └── Dtos.cs                           ← KnowledgeItemDto, ChunkDto, PackDto, GraphEntityDto
│   ├── Agent/
│   │   └── Dtos.cs                           ← AgentDto, ExecutionDto, MemoryDto, AgentConfigDto
│   ├── Workflow/
│   │   └── Dtos.cs                           ← WorkflowDefinitionDto, InstanceDto, TaskDto
│   ├── Search/
│   │   └── Dtos.cs                           ← SearchRequest, SearchResult, SavedSearchDto
│   ├── Connector/
│   │   └── Dtos.cs                           ← ConnectorDto, InstanceDto, SyncJobDto
│   ├── Analytics/
│   │   └── Dtos.cs                           ← DashboardDto, AgentAnalyticsDto, ReportRequest
│   ├── Notification/
│   │   └── Dtos.cs                           ← NotificationDto, PreferencesDto
│   └── Platform/
│       └── Dtos.cs                           ← AuditEventDto, FeatureFlagDto
│
├── Infrastructure/                           ← Couche Infrastructure (EF Core, Services, Adapters)
│   │
│   ├── Persistence/                          ← EF Core + PostgreSQL
│   │   ├── EaiosDbContext.cs                 ← DbContext principal (Global Query Filters, Interceptors)
│   │   ├── PlatformDbContext.cs              ← DbContext plateforme (non-tenant : Organizations, AuditLogs...)
│   │   ├── Configurations/                   ← IEntityTypeConfiguration<T> par entité
│   │   │   ├── Identity/
│   │   │   │   ├── UserConfiguration.cs
│   │   │   │   ├── SessionConfiguration.cs
│   │   │   │   ├── MfaCredentialConfiguration.cs
│   │   │   │   ├── ApiKeyConfiguration.cs
│   │   │   │   └── InvitationConfiguration.cs
│   │   │   ├── Organization/
│   │   │   │   ├── OrganizationConfiguration.cs
│   │   │   │   ├── WorkspaceConfiguration.cs
│   │   │   │   ├── DepartmentConfiguration.cs
│   │   │   │   └── MembershipConfiguration.cs
│   │   │   ├── AccessControl/
│   │   │   │   ├── RoleConfiguration.cs
│   │   │   │   ├── PermissionConfiguration.cs
│   │   │   │   ├── UserRoleConfiguration.cs
│   │   │   │   ├── PolicyConfiguration.cs
│   │   │   │   └── ResourceAclConfiguration.cs
│   │   │   ├── Resource/
│   │   │   │   ├── ResourceConfiguration.cs
│   │   │   │   ├── DocumentConfiguration.cs
│   │   │   │   ├── DocumentVersionConfiguration.cs
│   │   │   │   ├── FolderConfiguration.cs
│   │   │   │   ├── MetadataTemplateConfiguration.cs
│   │   │   │   ├── MetadataValueConfiguration.cs
│   │   │   │   ├── ResourceShareConfiguration.cs
│   │   │   │   └── LegalHoldConfiguration.cs
│   │   │   ├── Knowledge/
│   │   │   │   ├── KnowledgeItemConfiguration.cs
│   │   │   │   ├── KnowledgeChunkConfiguration.cs
│   │   │   │   ├── KnowledgeRelationConfiguration.cs
│   │   │   │   └── KnowledgePackConfiguration.cs
│   │   │   ├── Agent/
│   │   │   │   ├── AgentConfiguration.cs
│   │   │   │   ├── AgentExecutionConfiguration.cs
│   │   │   │   ├── AgentMemoryConfiguration.cs
│   │   │   │   └── PromptTemplateConfiguration.cs
│   │   │   ├── Workflow/
│   │   │   │   ├── WorkflowDefinitionConfiguration.cs
│   │   │   │   ├── WorkflowInstanceConfiguration.cs
│   │   │   │   └── WorkflowTaskConfiguration.cs
│   │   │   ├── Search/
│   │   │   │   ├── SavedSearchConfiguration.cs
│   │   │   │   └── EmbeddingConfiguration.cs
│   │   │   ├── Connector/
│   │   │   │   ├── ConnectorInstanceConfiguration.cs
│   │   │   │   └── SyncJobConfiguration.cs
│   │   │   ├── Analytics/
│   │   │   │   └── AnalyticsEventConfiguration.cs
│   │   │   └── Notification/
│   │   │       ├── NotificationConfiguration.cs
│   │   │       └── NotificationTemplateConfiguration.cs
│   │   │
│   │   ├── Migrations/                       ← EF Core migrations
│   │   │   └── (générées par dotnet ef)
│   │   │
│   │   ├── Interceptors/
│   │   │   ├── AuditSaveChangesInterceptor.cs ← Intercepte SaveChanges → émet AuditEvents
│   │   │   └── TenantSessionInterceptor.cs    ← SET app.current_tenant_id sur chaque connexion (RLS)
│   │   │
│   │   ├── Repositories/                     ← Implémentations Repository Pattern
│   │   │   ├── Base/
│   │   │   │   └── RepositoryBase.cs         ← Generic repository avec méthodes CRUD + pagination
│   │   │   ├── Identity/
│   │   │   │   ├── IUserRepository.cs
│   │   │   │   ├── UserRepository.cs
│   │   │   │   ├── ISessionRepository.cs
│   │   │   │   ├── SessionRepository.cs
│   │   │   │   ├── IApiKeyRepository.cs
│   │   │   │   └── ApiKeyRepository.cs
│   │   │   ├── Organization/
│   │   │   │   ├── IOrganizationRepository.cs
│   │   │   │   ├── OrganizationRepository.cs
│   │   │   │   ├── IWorkspaceRepository.cs
│   │   │   │   ├── WorkspaceRepository.cs
│   │   │   │   ├── IDepartmentRepository.cs
│   │   │   │   └── DepartmentRepository.cs
│   │   │   ├── AccessControl/
│   │   │   │   ├── IRoleRepository.cs
│   │   │   │   ├── RoleRepository.cs
│   │   │   │   ├── IPermissionRepository.cs
│   │   │   │   └── PermissionRepository.cs
│   │   │   ├── Resource/
│   │   │   │   ├── IResourceRepository.cs
│   │   │   │   ├── ResourceRepository.cs
│   │   │   │   ├── IFolderRepository.cs
│   │   │   │   └── FolderRepository.cs
│   │   │   ├── Knowledge/
│   │   │   │   ├── IKnowledgeItemRepository.cs
│   │   │   │   └── KnowledgeItemRepository.cs
│   │   │   ├── Agent/
│   │   │   │   ├── IAgentRepository.cs
│   │   │   │   └── AgentRepository.cs
│   │   │   ├── Workflow/
│   │   │   │   ├── IWorkflowRepository.cs
│   │   │   │   └── WorkflowRepository.cs
│   │   │   ├── Search/
│   │   │   │   └── SavedSearchRepository.cs
│   │   │   ├── Analytics/
│   │   │   │   └── AnalyticsRepository.cs
│   │   │   └── Notification/
│   │   │       └── NotificationRepository.cs
│   │   │
│   │   └── Seeds/
│   │       └── SystemPermissionsSeed.cs      ← Seed catalogue permissions + rôles système
│   │
│   ├── MultiTenancy/                         ← Système multi-tenant complet
│   │   ├── ITenantContext.cs                 ← Interface (déplacée depuis Application)
│   │   ├── TenantContext.cs                  ← Implémentation scoped
│   │   ├── TenantResolutionMiddleware.cs     ← Résolution : JWT claim → Header → Subdomain
│   │   └── TenantProvisioningService.cs     ← Création schema PG, bucket MinIO, collection Qdrant
│   │
│   ├── Security/                             ← Sécurité, Auth, Permissions
│   │   ├── TokenService.cs                   ← JWT RS256 + Refresh Token rotation (étendu)
│   │   ├── PasswordService.cs                ← Argon2id hashing / verification
│   │   ├── TotpService.cs                    ← MFA TOTP (génération secret, QR code, vérification)
│   │   ├── PermissionService.cs              ← Évaluation RBAC → ABAC → Resource Policy (3 couches)
│   │   └── ApiKeyService.cs                  ← Génération / validation clés API (format eak_xxx)
│   │
│   ├── Storage/                              ← Abstraction stockage fichiers
│   │   ├── IStorageService.cs
│   │   ├── LocalStorageService.cs            ← Dev : stockage local (remplace MinIO)
│   │   └── StorageOptions.cs                 ← Configuration storage
│   │
│   ├── Search/                               ← Abstraction moteur de recherche
│   │   ├── ISearchService.cs                 ← Interface search (fulltext + semantic)
│   │   └── InMemorySearchService.cs          ← Dev stub : simulation résultats
│   │
│   ├── AI/                                   ← Abstraction IA / LLM
│   │   ├── ILlmService.cs                    ← Interface LLM (génération, embeddings)
│   │   └── StubLlmService.cs                 ← Dev stub : réponses simulées plausibles
│   │
│   ├── Notifications/                        ← Service notifications
│   │   ├── INotificationService.cs
│   │   └── InMemoryNotificationService.cs    ← Dev : stockage en mémoire + log
│   │
│   ├── Audit/                                ← Audit service
│   │   ├── IAuditService.cs
│   │   └── AuditService.cs                   ← Enregistre AuditEvent dans PlatformDbContext
│   │
│   └── ServiceExtensions.cs                  ← AddInfrastructure() extension method DI
│
├── Middleware/                               ← ASP.NET Core Middleware + Filters
│   ├── RequestContext.cs                     ← CurrentTenant, CurrentUser, Correlation ID (étendu)
│   ├── CorrelationIdMiddleware.cs            ← Injection X-Correlation-ID
│   ├── BearerTokenMiddleware.cs              ← Validation JWT + peuplement ClaimsPrincipal
│   ├── TenantResolutionMiddleware.cs         ← Résolution tenant (déplacé ici)
│   ├── RateLimitingMiddleware.cs             ← Rate limiting par endpoint (ex: auth = 10/min)
│   ├── RequireTenantFilter.cs               ← AuthorizationFilter : bloque si pas de tenant
│   └── GlobalExceptionHandler.cs            ← IExceptionHandler → RFC 7807 ProblemDetails
│
├── Controllers/
│   └── V1/
│       ├── V1ApiController.cs                ← Base controller (TenantId, IsAuthenticated, helpers)
│       │
│       ├── AuthController.cs                 ← /v1/auth/* (login, MFA, refresh, logout, register,
│       │                                        verify-email, forgot-pwd, reset-pwd)
│       ├── UsersController.cs                ← /v1/users/* (me, avatar, password, api-keys, admin)
│       ├── OrganizationController.cs         ← /v1/organization (get, update, invitations)
│       ├── WorkspacesController.cs           ← /v1/workspaces (CRUD + members)
│       ├── DepartmentsController.cs          ← /v1/departments (CRUD + members)
│       │
│       ├── RolesController.cs                ← /v1/roles (CRUD + permissions)
│       ├── PermissionsController.cs          ← /v1/permissions + /v1/access/check
│       ├── PoliciesController.cs             ← /v1/policies (ABAC CRUD)
│       │
│       ├── ResourcesController.cs            ← /v1/resources (CRUD, upload, versioning,
│       │                                        metadata, shares, ACL, legal-hold)
│       ├── FoldersController.cs              ← /v1/folders (CRUD + move)
│       │
│       ├── KnowledgeController.cs            ← /v1/knowledge/* (items, packs, graph)
│       │
│       ├── AgentsController.cs               ← /v1/agents/* (CRUD, execute, stream, memories)
│       │
│       ├── WorkflowsController.cs            ← /v1/workflows/* (definitions, instances, tasks)
│       │
│       ├── SearchController.cs               ← /v1/search (hybrid, semantic, ask/RAG, saved)
│       │
│       ├── ConnectorsController.cs           ← /v1/connectors/* (catalog, instances, sync jobs)
│       │
│       ├── AnalyticsController.cs            ← /v1/analytics/* (dashboard, reports)
│       │
│       ├── NotificationsController.cs        ← /v1/notifications (list, mark-read, preferences)
│       │
│       └── AdminController.cs                ← /v1/admin/* (tenants, feature flags, audit logs)
│
├── appsettings.json                          ← Config (Connection strings, Security, Storage, AI)
├── appsettings.Development.json              ← Overrides dev (In-Memory / Local storage)
├── Program.cs                                ← Wiring DI complet, Middleware pipeline
└── backend.csproj                            ← Packages NuGet (EF Core, Npgsql, etc.)
```

---

## Proposed Changes — Détail par Fichier

### Phase 0 — Packages NuGet

#### [MODIFY] backend.csproj

Packages à ajouter :
```xml
<!-- EF Core + PostgreSQL -->
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Tools" Version="10.*" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.*" />

<!-- Sécurité / Auth -->
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.*" />
<PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.*" />
<PackageReference Include="Isopoh.Cryptography.Argon2" Version="2.*" />
<PackageReference Include="OtpNet" Version="1.*" />   <!-- TOTP MFA -->
<PackageReference Include="QRCoder" Version="1.*" />   <!-- QR Code MFA -->

<!-- Rate Limiting (natif .NET 7+) -->
<!-- Utilise AspNetCoreRateLimit ou natif RateLimiter -->

<!-- Validation -->
<PackageReference Include="FluentValidation.AspNetCore" Version="11.*" />

<!-- Observabilité -->
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.*" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.*" />
<PackageReference Include="OpenTelemetry.Instrumentation.EntityFrameworkCore" Version="1.*" />

<!-- Health Checks -->
<PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore" Version="10.*" />
<PackageReference Include="AspNetCore.HealthChecks.NpgSql" Version="9.*" />

<!-- Stockage / S3 compatible (MinIO) -->
<PackageReference Include="AWSSDK.S3" Version="3.*" />

<!-- Background Jobs -->
<PackageReference Include="Hangfire.AspNetCore" Version="1.*" />
<PackageReference Include="Hangfire.PostgreSql" Version="1.*" />
```

---

### Phase 1 — Domain Layer

#### [NEW] Domain/Shared/Primitives/Entity.cs
```csharp
public abstract class Entity<TId>
{
    public TId Id { get; protected set; }
    public DateTime CreatedAt { get; protected set; }
    public DateTime UpdatedAt { get; protected set; }
}
```

#### [NEW] Domain/Shared/Primitives/TenantEntity.cs
Classe de base OBLIGATOIRE pour toutes les entités métier :
- `Id : Guid` (UUID v7)
- `OrganizationId : Guid` — injection automatique par DbContext
- `IsDeleted / DeletedAt / DeletedBy` — soft delete
- `CreatedAt / CreatedBy / UpdatedAt / UpdatedBy` — audit
- `DomainEvents` — liste des events à dispatcher
- Méthodes internes : `SetOrganizationId()`, `SetCreated()`, `SetUpdated()`, `SetSoftDeleted()`

#### [NEW] Domain/Shared/Interfaces/ITenantScoped.cs
```csharp
public interface ITenantScoped { Guid OrganizationId { get; } }
```

#### [NEW] Domain/Shared/Interfaces/ISoftDeletable.cs
```csharp
public interface ISoftDeletable { bool IsDeleted { get; } DateTime? DeletedAt { get; } Guid? DeletedBy { get; } }
```

#### [NEW] Domain/Shared/Interfaces/IAuditable.cs
```csharp
public interface IAuditable { DateTime CreatedAt { get; } Guid? CreatedBy { get; } DateTime UpdatedAt { get; } Guid? UpdatedBy { get; } }
```

#### [NEW] Domain/Shared/Interfaces/IDomainEvent.cs
```csharp
public interface IDomainEvent { Guid Id { get; } DateTime OccurredOn { get; } }
```

#### [NEW] Domain/Shared/Events/DomainEvents.cs
Catalogue complet des Domain Events (conforme entity catalog v2) :
- Identity : `UserRegisteredEvent`, `UserActivatedEvent`, `UserSuspendedEvent`
- Organization : `TenantProvisionedEvent`, `WorkspaceCreatedEvent`
- Resource : `DocumentUploadedEvent`, `DocumentIndexingRequestedEvent`, `DocumentParsedEvent`, `DocumentDeletedEvent`
- Knowledge : `KnowledgeExtractionRequestedEvent`, `KnowledgeItemCreatedEvent`, `EmbeddingGenerationRequestedEvent`
- Agent : `AgentExecutionStartedEvent`, `AgentExecutionCompletedEvent`, `AgentHumanInputRequiredEvent`
- Workflow : `WorkflowInstanceStartedEvent`, `WorkflowTaskAssignedEvent`, `WorkflowTaskCompletedEvent`, `WorkflowSlaBreachedEvent`

#### [MODIFY] Domain/Identity/Entities.cs (remplace IdentityAndOrganization.cs)
Entités : `User` (étendu complet), `Session`, `MfaCredential`, `ApiKey`, `Invitation`
Enums : `UserStatus`, `MfaMethod`, `InvitationStatus`

#### [NEW] Domain/Organization/Entities.cs
Entités : `Organization` (complet avec quotas), `Workspace`, `Department`, `Team`, `Membership`
Enums : `OrganizationStatus`, `WorkspaceType`, `WorkspaceStatus`, `DepartmentStatus`, `MembershipType`, `MembershipStatus`

#### [NEW] Domain/AccessControl/Entities.cs
Entités : `Role`, `RolePermission`, `Permission`, `UserRole`, `Policy`, `ResourceAcl`
Enums : `RoleScope`, `PolicyType`, `PolicyEffect`, `PrincipalType`, `AclEffect`
Rôles système prédéfinis en constantes.

#### [NEW] Domain/Resource/Entities.cs
Entités : `Resource`, `Document`, `DocumentVersion`, `Folder`, `MetadataTemplate`, `MetadataValue`, `ResourceShare`, `ResourceTag`, `LegalHold`
Value Objects : `MetadataFieldDefinition`
Enums : `ResourceClassification`, `ResourceStatus`, `IndexingStatus`, `DocumentVersionSource`, `ShareTargetType`, `SharePermission`, `LegalHoldStatus`, `FolderStatus`

#### [NEW] Domain/Knowledge/Entities.cs
Entités : `KnowledgeItem`, `KnowledgeChunk`, `KnowledgeRelation`, `KnowledgePack`
Enums : `KnowledgeItemType`, `KnowledgeItemSource`, `KnowledgeItemStatus`, `KnowledgePackStatus`, `KnowledgeRelationSource`

#### [NEW] Domain/Agent/Entities.cs
Entités : `Agent`, `AgentVersion`, `AgentExecution`, `AgentMemory`, `AgentTool`, `PromptTemplate`
Value Objects : `AgentLlmConfig` (Provider, Model, Temperature, MaxOutputTokens, UseStreaming)
Enums : `AgentType`, `AgentStatus`, `AgentVisibility`, `AgentExecutionStatus`, `AgentMemoryType`, `PromptRole`

#### [NEW] Domain/Workflow/Entities.cs
Entités : `WorkflowDefinition`, `WorkflowDefinitionVersion`, `WorkflowInstance`, `WorkflowTask`, `WorkflowStepExecution`
Enums : `WorkflowDefinitionStatus`, `WorkflowTriggerType`, `WorkflowInstanceStatus`, `WorkflowTaskStatus`, `WorkflowTaskAssigneeType`

#### [NEW] Domain/Search/Entities.cs
Entités : `SavedSearch`, `Embedding`
Enums : `SearchType`

#### [NEW] Domain/Connector/Entities.cs
Entités : `ConnectorDefinition`, `ConnectorInstance`, `SyncJob`, `SyncExecution`
Enums : `ConnectorCategory`, `ConnectorAuthType`, `ConnectorInstanceStatus`, `SyncHealth`, `SyncDirection`, `SyncJobStatus`, `ConflictResolutionStrategy`

#### [NEW] Domain/Analytics/Entities.cs
Entité : `AnalyticsEvent` (Append-Only, partitionné par mois)

#### [NEW] Domain/Notification/Entities.cs
Entités : `Notification`, `NotificationTemplate`
Enums : `NotificationChannel`, `NotificationPriority`, `NotificationStatus`

#### [NEW] Domain/Platform/Entities.cs
Entités : `AuditEvent` (NON TenantEntity — Append-Only immuable), `FeatureFlag`, `FeatureFlagOverride`
Enums : `AuditEventResult`, `FeatureFlagType`

---

### Phase 2 — Application Layer (DTOs & Service Interfaces)

#### [NEW] Application/Common/Models/PagedResult.cs
```csharp
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
}
```

#### [NEW] Application/Common/Models/ApiResponse.cs
```csharp
public static class ApiResponse
{
    public static object Ok<T>(T data) => new { data };
    public static object Created<T>(T data) => new { data };
}
```

#### [NEW] Application/Common/Interfaces/ICurrentUser.cs
```csharp
public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? OrganizationId { get; }
    bool IsAuthenticated { get; }
    bool HasRole(string role);
    bool HasPermission(string permission);
}
```

#### [NEW] Application/Common/Interfaces/ITenantContext.cs
```csharp
public interface ITenantContext
{
    Guid OrganizationId { get; }
    string TenantSchemaName { get; }
    bool IsResolved { get; }
    void SetTenant(Guid organizationId);
}
```

#### [NEW] Application/Identity/Dtos.cs
Records : `LoginRequest`, `LoginResponse`, `MfaLoginRequest`, `RefreshRequest`, `RegisterRequest`,
`VerifyEmailRequest`, `ForgotPasswordRequest`, `ResetPasswordRequest`, `ChangePasswordRequest`,
`UpdateProfileRequest`, `UserDto`, `UserSummaryDto`, `SessionDto`, `ApiKeyDto`, `CreateApiKeyRequest`,
`MfaTotpSetupDto`, `EnableTotpRequest`, `InvitationDto`

#### [NEW] Application/Organization/Dtos.cs
Records : `OrganizationDto`, `UpdateOrganizationRequest`, `WorkspaceDto`, `CreateWorkspaceRequest`,
`UpdateWorkspaceRequest`, `DepartmentDto`, `CreateDepartmentRequest`, `UpdateDepartmentRequest`,
`MemberDto`, `AddMemberRequest`, `CreateInvitationRequest`, `InvitationDto`

#### [NEW] Application/AccessControl/Dtos.cs
Records : `RoleDto`, `CreateRoleRequest`, `UpdateRoleRequest`, `PermissionDto`, `UserRoleDto`,
`AssignRoleRequest`, `PolicyDto`, `CreatePolicyRequest`, `ResourceAclDto`, `CreateAclRequest`,
`AccessCheckRequest`, `AccessCheckResult`

#### [NEW] Application/Resource/Dtos.cs
Records : `ResourceDto`, `DocumentDto`, `DocumentSummaryDto`, `UploadRequest`, `MultipartInitiateRequest`,
`MultipartCompleteRequest`, `ChunkUploadResult`, `DocumentVersionDto`, `FolderDto`, `CreateFolderRequest`,
`MetadataDto`, `UpdateMetadataRequest`, `ResourceShareDto`, `CreateShareRequest`, `PublicLinkRequest`,
`LegalHoldDto`, `CreateLegalHoldDto`, `ReleaseLegalHoldRequest`, `ResourceAclSummaryDto`

#### [NEW] Application/Knowledge/Dtos.cs
Records : `KnowledgeItemDto`, `CreateKnowledgeItemRequest`, `UpdateKnowledgeItemRequest`,
`ValidateItemRequest`, `KnowledgeChunkDto`, `KnowledgeRelationDto`, `KnowledgePackDto`,
`CreatePackRequest`, `GraphEntityDto`, `GraphQueryRequest`

#### [NEW] Application/Agent/Dtos.cs
Records : `AgentDto`, `CreateAgentRequest`, `UpdateAgentRequest`, `AgentLlmConfigDto`,
`ExecuteAgentRequest`, `AgentExecutionDto`, `ExecutionMetricsDto`, `CitationDto`,
`AgentMemoryDto`, `CloneAgentRequest`, `HumanInputRequest`

#### [NEW] Application/Workflow/Dtos.cs
Records : `WorkflowDefinitionDto`, `CreateWorkflowRequest`, `WorkflowInstanceDto`,
`StartWorkflowRequest`, `WorkflowTaskDto`, `CompleteTaskRequest`, `ReassignTaskRequest`

#### [NEW] Application/Search/Dtos.cs
Records : `SearchRequest`, `SearchFilters`, `SearchResult`, `SearchFacet`, `SearchResponse`,
`SemanticSearchRequest`, `AskRequest`, `AskResponse`, `Citation`, `SavedSearchDto`, `CreateSavedSearchRequest`

#### [NEW] Application/Connector/Dtos.cs
Records : `ConnectorDefinitionDto`, `ConnectorInstanceDto`, `CreateConnectorRequest`,
`SyncJobDto`, `CreateSyncJobRequest`, `ConnectionTestResult`, `SyncExecutionDto`

#### [NEW] Application/Analytics/Dtos.cs
Records : `DashboardDto`, `UsageMetricsDto`, `AgentAnalyticsDto`, `WorkflowAnalyticsDto`,
`SearchAnalyticsDto`, `GenerateReportRequest`

#### [NEW] Application/Notification/Dtos.cs
Records : `NotificationDto`, `NotificationPreferencesDto`, `UpdatePreferencesRequest`

#### [NEW] Application/Platform/Dtos.cs
Records : `AuditEventDto`, `AuditQueryRequest`, `FeatureFlagDto`, `UpdateFeatureFlagRequest`, `TenantSummaryDto`

---

### Phase 3 — Infrastructure Layer

#### [NEW] Infrastructure/Persistence/EaiosDbContext.cs
DbContext principal avec :
- Tous les `DbSet<T>` pour les entités métier tenant-scoped
- `OnModelCreating` avec **Global Query Filters automatiques** (boucle sur toutes entités `ITenantScoped` → filtre `OrganizationId + IsDeleted`)
- `HasDefaultSchema(_tenantContext.TenantSchemaName)` — schema-per-tenant
- `SaveChangesAsync` intercepté : injection `OrganizationId`, `CreatedAt/By`, `UpdatedAt/By`, force soft-delete
- `ApplyConfigurationsFromAssembly`

#### [NEW] Infrastructure/Persistence/PlatformDbContext.cs
DbContext pour les données plateforme (non-tenant) :
- `DbSet<Organization>`, `DbSet<AuditEvent>`, `DbSet<FeatureFlag>`, `DbSet<ConnectorDefinition>`
- Schéma `platform` + `audit` + `identity`
- Pas de Global Query Filter tenant (ces tables sont cross-tenant)

#### [NEW] Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptor.cs
`ISaveChangesInterceptor` qui :
- Détecte les changements sur entités `IAuditable`
- Capture les `OldValues` / `NewValues`
- Crée un `AuditEvent` dans `PlatformDbContext` (append-only)

#### [NEW] Infrastructure/Persistence/Interceptors/TenantSessionInterceptor.cs
`DbConnectionInterceptor` qui exécute `SET app.current_tenant_id = '{orgId}'` sur chaque ouverture de connexion (active le Row-Level Security PostgreSQL).

#### [NEW] Infrastructure/Persistence/Repositories/Base/RepositoryBase.cs
Generic repository :
```csharp
public abstract class RepositoryBase<T, TId>(EaiosDbContext db) where T : TenantEntity
{
    public Task<T?> GetByIdAsync(TId id, CancellationToken ct = default);
    public Task<PagedResult<T>> GetPagedAsync(int page, int pageSize, ...);
    public Task AddAsync(T entity, CancellationToken ct = default);
    public void Update(T entity);
    public void Delete(T entity);   // → soft-delete via DbContext
    public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct);
}
```

#### [NEW] Infrastructure/Persistence/Repositories/Identity/UserRepository.cs
```csharp
public interface IUserRepository : IRepositoryBase<User, Guid>
{
    Task<User?> FindByEmailAsync(Guid organizationId, string email, CancellationToken ct);
    Task<bool> EmailExistsAsync(Guid organizationId, string email, CancellationToken ct);
    Task<PagedResult<User>> SearchAsync(Guid organizationId, string? search, UserStatus? status, ...);
}
```

#### [NEW] Infrastructure/Persistence/Repositories/Identity/SessionRepository.cs
```csharp
public interface ISessionRepository
{
    Task<Session?> FindByRefreshTokenHashAsync(string hash, CancellationToken ct);
    Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct);
    Task RevokeAsync(Guid sessionId, string reason, CancellationToken ct);
}
```

#### [NEW] Infrastructure/Persistence/Repositories/Identity/ApiKeyRepository.cs
```csharp
public interface IApiKeyRepository
{
    Task<ApiKey?> FindByKeyHashAsync(string hash, CancellationToken ct);
    Task<IReadOnlyList<ApiKey>> GetByUserIdAsync(Guid userId, CancellationToken ct);
    Task RevokeAsync(Guid keyId, Guid userId, CancellationToken ct);
}
```

#### [NEW] Infrastructure/Persistence/Repositories/Resource/ResourceRepository.cs
```csharp
public interface IResourceRepository : IRepositoryBase<Resource, Guid>
{
    Task<PagedResult<Document>> SearchDocumentsAsync(DocumentSearchParams p, CancellationToken ct);
    Task<Document?> GetDocumentWithVersionsAsync(Guid resourceId, CancellationToken ct);
    Task<IReadOnlyList<DocumentVersion>> GetVersionsAsync(Guid documentId, CancellationToken ct);
}
```

#### Repositories pour tous les autres modules
_(même pattern — interface + implémentation EF Core)_

#### [NEW] Infrastructure/Persistence/Seeds/SystemPermissionsSeed.cs
Seed initial :
- Catalogue complet des permissions (40+ codes)
- Rôles système : `platform.owner`, `platform.admin`, `org.admin`, `org.member`, `org.guest`, `workspace.admin`, `workspace.member`, `dept.manager`, `dept.member`
- Assignation des permissions aux rôles système

#### [NEW] Infrastructure/Persistence/Configurations/Identity/UserConfiguration.cs
```csharp
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", "identity");
        builder.HasIndex(u => new { u.OrganizationId, u.NormalizedEmail }).IsUnique();
        builder.HasIndex(u => new { u.OrganizationId, u.Status })
               .HasFilter("is_deleted = false");
        builder.Property(u => u.PasswordHash).HasColumnName("password_hash");
        builder.Property(u => u.NotificationPreferences).HasColumnType("jsonb");
        // ... toutes les colonnes
    }
}
```
_(Configurations similaires pour toutes les entités)_

#### [NEW] Infrastructure/MultiTenancy/TenantContext.cs
```csharp
public sealed class TenantContext : ITenantContext
{
    private Guid? _organizationId;
    public Guid OrganizationId => _organizationId ?? throw new TenantNotResolvedException();
    public string TenantSchemaName => $"org_{_organizationId?.ToString("N")}";
    public bool IsResolved => _organizationId.HasValue;
    public void SetTenant(Guid organizationId) { /* une seule fois par requête */ }
}
```

#### [NEW] Infrastructure/MultiTenancy/TenantResolutionMiddleware.cs
Résolution dans cet ordre :
1. JWT claim `org_id` (prioritaire si authentifié)
2. Header `X-Organization-Id`
3. Subdomain (ex: `acme.eaios.io`)
→ Retourne `401` avec ProblemDetails si non résolu et endpoint protégé

#### [NEW] Infrastructure/Security/TokenService.cs (étendu)
- JWT signé RS256 (asymétrique) avec claims : `sub`, `org_id`, `session_id`, `roles`, `exp`
- Refresh Token rotation obligatoire
- Méthodes : `IssueAccessToken()`, `IssueRefreshToken()`, `ValidateAccessToken()`, `HashToken()`

#### [NEW] Infrastructure/Security/PasswordService.cs
```csharp
public static class PasswordService
{
    public static string Hash(string password);        // Argon2id, 64MB memory, 3 iterations
    public static bool Verify(string password, string hash);
    public static bool MeetsComplexityRequirements(string password); // min 12 chars, maj, chiffre, spécial
}
```

#### [NEW] Infrastructure/Security/TotpService.cs
```csharp
public sealed class TotpService
{
    public TotpSetup GenerateSetup(string issuer, string email);    // Base32 secret + QR code data URI
    public bool Verify(string secret, string code, int toleranceSteps = 1);
    public string[] GenerateBackupCodes(int count = 10);
}
```

#### [NEW] Infrastructure/Security/PermissionService.cs
```csharp
public sealed class PermissionService(IUserRepository users, IRoleRepository roles) : IPermissionService
{
    // Évaluation 3 couches RBAC → ABAC → Resource Policy
    public async Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken ct);
    public async Task<AccessCheckResult> CheckAccessAsync(Guid userId, string permission,
        Guid? resourceId, string? resourceType, CancellationToken ct);
}
```

#### [NEW] Infrastructure/Security/ApiKeyService.cs
```csharp
public sealed class ApiKeyService
{
    // Format: eak_{8chars_prefix}_{64chars_random}
    public (string fullKey, string prefix, string hash) Generate();
    public string? ValidateAndGetPrefix(string key);
}
```

#### [NEW] Infrastructure/Storage/IStorageService.cs
```csharp
public interface IStorageService
{
    Task<string> UploadAsync(Stream content, string fileName, string contentType, string organizationId, CancellationToken ct);
    Task<string> GetSignedDownloadUrlAsync(string storageKey, TimeSpan expiry, CancellationToken ct);
    Task<string> GetSignedPreviewUrlAsync(string storageKey, TimeSpan expiry, CancellationToken ct);
    Task DeleteAsync(string storageKey, CancellationToken ct);
    Task<UploadSession> InitiateMultipartAsync(string fileName, long totalSize, string organizationId, CancellationToken ct);
    Task UploadChunkAsync(string uploadId, int chunkIndex, Stream data, string checksum, CancellationToken ct);
    Task<string> CompleteMultipartAsync(string uploadId, IReadOnlyList<ChunkInfo> chunks, CancellationToken ct);
    Task AbortMultipartAsync(string uploadId, CancellationToken ct);
}
```

#### [NEW] Infrastructure/Storage/LocalStorageService.cs
Implémentation dev : stockage sur disque local (`wwwroot/uploads/`), URLs locales.

#### [NEW] Infrastructure/AI/ILlmService.cs
```csharp
public interface ILlmService
{
    Task<string> GenerateAsync(string systemPrompt, string userInput, AgentLlmConfig config, CancellationToken ct);
    IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userInput, AgentLlmConfig config, CancellationToken ct);
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct);
    Task<IReadOnlyList<SearchChunk>> RetrieveRelevantChunksAsync(string query, Guid organizationId, int topK, CancellationToken ct);
}
```

#### [NEW] Infrastructure/AI/StubLlmService.cs
Dev stub : réponses simulées plausibles avec délai artificiel pour tester le streaming SSE.

#### [NEW] Infrastructure/Audit/AuditService.cs
```csharp
public sealed class AuditService(PlatformDbContext db, ICurrentUser currentUser, IHttpContextAccessor http) : IAuditService
{
    public Task LogAsync(string action, string? resourceType = null, Guid? resourceId = null,
        object? oldValues = null, object? newValues = null, AuditEventResult result = AuditEventResult.Success,
        CancellationToken ct = default);
}
```

#### [NEW] Infrastructure/Notifications/InMemoryNotificationService.cs
Dev : stocke les notifications en mémoire concurrente + log console.

#### [NEW] Infrastructure/ServiceExtensions.cs
```csharp
public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
{
    // EF Core (conditionnel : UseNpgsql si connection string présente, sinon InMemory/dev)
    // Multi-Tenancy
    // Security services
    // Storage
    // AI (stub en dev)
    // Audit
    // Notifications
    // Repositories (tous)
    // Health checks
    // Rate limiting
    return services;
}
```

---

### Phase 4 — Middleware Pipeline

#### [MODIFY] Middleware/RequestContext.cs (étendu)
Ajout :
- `CurrentUser` service scoped (résolution depuis ClaimsPrincipal)
- `RequirePermissionAttribute` — attribute custom pour décorateur controllers
- `RequireRoleAttribute`

#### [NEW] Middleware/GlobalExceptionHandler.cs
```csharp
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    // → ValidationException → 422 avec errors field
    // → UnauthorizedAccessException → 401
    // → ForbiddenException → 403
    // → NotFoundException → 404
    // → ConflictException → 409
    // → TenantNotResolvedException → 401
    // → Exception générique → 500
    // Format : RFC 7807 ProblemDetails avec traceId = X-Correlation-ID
}
```

#### [NEW] Middleware/RateLimitingMiddleware.cs
Configuré via options :
- `/v1/auth/login` : 10 req/min par IP
- `/v1/auth/forgot-password` : 5 req/heure par email
- Global : 1000 req/min par tenant

---

### Phase 5 — Controllers (détail complet)

#### [MODIFY] Controllers/V1/V1ApiController.cs (étendu)
```csharp
public abstract class V1ApiController(CurrentTenant tenant, ICurrentUser currentUser) : ControllerBase
{
    protected Guid TenantId => tenant.Id ?? throw...;
    protected Guid CurrentUserId => currentUser.UserId ?? throw...;
    protected bool IsAuthenticated => currentUser.IsAuthenticated;
    protected bool IsOrganizationAdmin => currentUser.HasRole("org.admin");
    protected bool IsPlatformAdmin => currentUser.HasRole("platform.admin");
    protected IActionResult AuthenticationRequired();
    protected IActionResult Forbidden();
    protected IActionResult NotFoundProblem(string detail = "Resource not found");
    protected IActionResult ConflictProblem(string detail);
    protected IActionResult UnprocessableProblem(string detail);
    protected IActionResult Accepted(string statusUrl, object data);
}
```

#### [MODIFY] Controllers/V1/AuthController.cs (complet)
Endpoints :
- `POST /v1/auth/bootstrap` (dev only)
- `POST /v1/auth/login` — Argon2id verify, MFA check, Session creation, rate limit 10/min
- `POST /v1/auth/login/mfa` — TOTP/SMS/backup code verification
- `POST /v1/auth/refresh` — Refresh token rotation
- `POST /v1/auth/logout` — Révoque session courante
- `POST /v1/auth/logout/all` — Révoque toutes les sessions
- `POST /v1/auth/register` — Invitation-based registration
- `POST /v1/auth/verify-email` — Email confirmation token
- `POST /v1/auth/forgot-password` — Silent (anti-enum), rate limit 5/heure
- `POST /v1/auth/reset-password` — Token reset, révoque toutes sessions
- `GET /v1/auth/mfa/totp/setup` — Génère secret + QR code Data URI
- `POST /v1/auth/mfa/totp/enable` — Confirme TOTP, génère backup codes
- `DELETE /v1/auth/mfa/{method}` — Désactive méthode MFA

#### [NEW] Controllers/V1/UsersController.cs
- `GET /v1/users/me` — Profil complet avec rôles + permissions
- `PUT /v1/users/me` — Mise à jour profil (langue, timezone, préférences)
- `POST /v1/users/me/avatar` — Upload avatar (multipart, resize 256x256)
- `POST /v1/users/me/change-password` — Argon2id verify old + hash new
- `GET /v1/users/me/api-keys` — Liste (sans clé complète)
- `POST /v1/users/me/api-keys` — Génère `eak_xxx`, retourne une seule fois
- `DELETE /v1/users/me/api-keys/{keyId}`
- `GET /v1/users` — Liste paginée (admin)
- `GET /v1/users/{userId}`
- `POST /v1/users/{userId}/suspend` — Suspend + révoque sessions
- `POST /v1/users/{userId}/activate`
- `DELETE /v1/users/{userId}` — Soft delete + planifie anonymisation RGPD
- `GET /v1/users/{userId}/sessions`
- `DELETE /v1/users/{userId}/sessions/{sessionId}`
- `GET /v1/users/{userId}/roles`
- `POST /v1/users/{userId}/roles`
- `DELETE /v1/users/{userId}/roles/{roleId}`

#### [MODIFY] Controllers/V1/OrganizationController.cs (étendu)
- `GET /v1/organization`
- `PUT /v1/organization`
- `GET /v1/organization/invitations`
- `POST /v1/organization/invitations` — Génère token, envoie email, quota check
- `DELETE /v1/organization/invitations/{invitationId}`
- `POST /v1/organization/invitations/{invitationId}/resend`

#### [NEW] Controllers/V1/RolesController.cs
- `GET /v1/roles` — Avec nb permissions + nb users
- `POST /v1/roles`
- `GET /v1/roles/{roleId}`
- `PUT /v1/roles/{roleId}` — Bloqué si `IsSystem = true`
- `DELETE /v1/roles/{roleId}` — Vérifie qu'aucun user assigné
- `GET /v1/roles/{roleId}/permissions`
- `PUT /v1/roles/{roleId}/permissions` — Replace complet (idempotent)

#### [NEW] Controllers/V1/PermissionsController.cs
- `GET /v1/permissions` — Catalogue groupé par module
- `POST /v1/access/check` — Évaluation RBAC→ABAC→ResourcePolicy avec layers expliqués

#### [NEW] Controllers/V1/PoliciesController.cs
- `GET /v1/policies`
- `POST /v1/policies` — Validation expression CEL (syntaxe)
- `GET /v1/policies/{policyId}`
- `PUT /v1/policies/{policyId}`
- `DELETE /v1/policies/{policyId}`

#### [NEW] Controllers/V1/ResourcesController.cs
- `GET /v1/resources` — Paginé + filtres (type, classification, status, tag, folderId, workspaceId)
- `GET /v1/resources/{resourceId}` — Avec versions, metadata, shares, ACL
- `PUT /v1/resources/{resourceId}` — Metadata descriptives
- `DELETE /v1/resources/{resourceId}` — Soft delete (vérifie LegalHold)
- `POST /v1/resources/{resourceId}/restore`
- `DELETE /v1/resources/{resourceId}/permanent` — Supprime MinIO + ES + Qdrant (admin)
- `POST /v1/resources/upload` — Fichiers ≤ 10MB, virus scan stub, déclenche pipeline indexation
- `POST /v1/resources/upload/multipart/initiate`
- `PUT /v1/resources/upload/multipart/{uploadId}/chunk/{chunkIndex}`
- `POST /v1/resources/upload/multipart/{uploadId}/complete`
- `DELETE /v1/resources/upload/multipart/{uploadId}`
- `GET /v1/resources/{resourceId}/versions`
- `POST /v1/resources/{resourceId}/versions` — Nouvelle version
- `POST /v1/resources/{resourceId}/versions/{versionId}/restore` — Rollback
- `GET /v1/resources/{resourceId}/versions/{versionId}/download` — Signed URL
- `GET /v1/resources/{resourceId}/download` — Version courante
- `GET /v1/resources/{resourceId}/preview` — URL preview signée
- `GET /v1/resources/{resourceId}/metadata`
- `PUT /v1/resources/{resourceId}/metadata` — Upsert MetadataValues
- `GET /v1/resources/{resourceId}/shares`
- `POST /v1/resources/{resourceId}/shares` — Partage interne + notification
- `POST /v1/resources/{resourceId}/shares/public-link` — Token UUID + URL publique
- `DELETE /v1/resources/{resourceId}/shares/{shareId}`
- `GET /v1/resources/{resourceId}/acl`
- `POST /v1/resources/{resourceId}/acl` — Règle Allow/Deny explicite
- `DELETE /v1/resources/{resourceId}/acl/{aclId}`
- `GET /v1/resources/{resourceId}/legal-hold`
- `POST /v1/resources/{resourceId}/legal-hold` — Bloque suppression
- `DELETE /v1/resources/{resourceId}/legal-hold/{holdId}` — Libère + fournit raison

#### [NEW] Controllers/V1/FoldersController.cs
- `GET /v1/folders` — Arborescence (avec workspaceId/departmentId filter)
- `POST /v1/folders` — Calcul Path, max depth 10
- `GET /v1/folders/{folderId}`
- `PUT /v1/folders/{folderId}` — Rename/move (recalcule Path descendants)
- `DELETE /v1/folders/{folderId}` — Vérifie vide, refuse IsSystemFolder
- `POST /v1/folders/{folderId}/move` — Recalcul récursif des Path

#### [NEW] Controllers/V1/KnowledgeController.cs
- `GET /v1/knowledge/items`
- `POST /v1/knowledge/items` — Génère embeddings async
- `GET /v1/knowledge/items/{itemId}`
- `PUT /v1/knowledge/items/{itemId}` — Rerend embeddings si contenu changé
- `DELETE /v1/knowledge/items/{itemId}` — Soft delete + supprime Qdrant
- `POST /v1/knowledge/items/{itemId}/publish`
- `POST /v1/knowledge/items/{itemId}/validate` — Human review
- `GET /v1/knowledge/packs`
- `POST /v1/knowledge/packs`
- `GET /v1/knowledge/packs/{packId}`
- `PUT /v1/knowledge/packs/{packId}`
- `GET /v1/knowledge/packs/{packId}/export` — Génère ZIP async
- `POST /v1/knowledge/packs/import` — Parse + réindexe async (202 Accepted)
- `GET /v1/knowledge/graph/entities`
- `GET /v1/knowledge/graph/entities/{entityId}` — Vue 360° relations
- `GET /v1/knowledge/graph/relations`
- `POST /v1/knowledge/graph/query` — Cypher read-only stub

#### [NEW] Controllers/V1/AgentsController.cs
- `GET /v1/agents` — Catalogue avec filtre visibility
- `POST /v1/agents` — Status = Draft
- `GET /v1/agents/{agentId}`
- `PUT /v1/agents/{agentId}` — Repasse en Draft si Published
- `DELETE /v1/agents/{agentId}` — Vérifie pas d'exécution en cours
- `POST /v1/agents/{agentId}/publish` — Crée AgentVersion snapshot
- `POST /v1/agents/{agentId}/deprecate`
- `GET /v1/agents/{agentId}/versions`
- `POST /v1/agents/{agentId}/clone`
- `POST /v1/agents/{agentId}/execute` — Sync (timeout 60s) / async (202)
- `POST /v1/agents/{agentId}/execute/stream` — SSE streaming tokens
- `GET /v1/agents/executions` — Liste avec filtres
- `GET /v1/agents/executions/{executionId}`
- `POST /v1/agents/executions/{executionId}/cancel`
- `POST /v1/agents/executions/{executionId}/human-input` — Reprend exécution suspendue
- `GET /v1/agents/{agentId}/memories`
- `DELETE /v1/agents/{agentId}/memories` — Reset complet
- `DELETE /v1/agents/{agentId}/memories/{memoryId}`

#### [NEW] Controllers/V1/WorkflowsController.cs
- `GET /v1/workflows/definitions`
- `POST /v1/workflows/definitions` — Valide graphe JSON
- `GET /v1/workflows/definitions/{definitionId}`
- `PUT /v1/workflows/definitions/{definitionId}` — Bloqué si Published
- `DELETE /v1/workflows/definitions/{definitionId}` — Bloqué si a des instances
- `POST /v1/workflows/definitions/{definitionId}/publish` — Crée WorkflowDefinitionVersion
- `GET /v1/workflows/definitions/{definitionId}/versions`
- `POST /v1/workflows/instances` — Démarre exécution
- `GET /v1/workflows/instances` — Tableau de bord
- `GET /v1/workflows/instances/{instanceId}`
- `POST /v1/workflows/instances/{instanceId}/cancel` — Annule + notifie
- `POST /v1/workflows/instances/{instanceId}/pause`
- `POST /v1/workflows/instances/{instanceId}/resume`
- `GET /v1/workflows/tasks` — Inbox personnel de l'utilisateur courant
- `GET /v1/workflows/tasks/{taskId}`
- `POST /v1/workflows/tasks/{taskId}/complete` — Fait avancer le workflow
- `POST /v1/workflows/tasks/{taskId}/reassign` — Notifie nouveau assigné

#### [NEW] Controllers/V1/SearchController.cs
- `POST /v1/search` — Hybrid BM25+Dense, RRF fusion, reranking, facettes
- `POST /v1/search/semantic` — Dense retrieval seul
- `POST /v1/search/ask` — RAG complet : retrieve → rerank → prompt → generate → citations
- `GET /v1/search/suggestions` — Autocomplete (min 2 chars)
- `GET /v1/search/saved`
- `POST /v1/search/saved` — + alert cron si alertEnabled
- `DELETE /v1/search/saved/{savedSearchId}`

#### [NEW] Controllers/V1/ConnectorsController.cs
- `GET /v1/connectors/catalog` — Catalogue global connecteurs
- `GET /v1/connectors/instances`
- `POST /v1/connectors/instances` — Chiffre credentials AES-256
- `GET /v1/connectors/instances/{instanceId}`
- `PUT /v1/connectors/instances/{instanceId}`
- `DELETE /v1/connectors/instances/{instanceId}`
- `POST /v1/connectors/instances/{instanceId}/test` — Test connexion + latence
- `GET /v1/connectors/instances/{instanceId}/sync-jobs`
- `POST /v1/connectors/instances/{instanceId}/sync-jobs`
- `POST /v1/connectors/instances/{instanceId}/sync-jobs/{jobId}/run` — 202 Accepted
- `GET /v1/connectors/sync-executions/{executionId}`

#### [NEW] Controllers/V1/AnalyticsController.cs
- `GET /v1/analytics/dashboard` — KPIs : users actifs, docs uploadés, searches, agent executions, storage
- `GET /v1/analytics/search` — Queries populaires, taux de click, zero-result
- `GET /v1/analytics/agents` — Tokens consommés, coûts USD, latence, taux de succès
- `GET /v1/analytics/workflows` — Taux completion, délai moyen, SLA respectés
- `POST /v1/analytics/reports` — Génère rapport (202 Accepted, stocke dans MinIO)

#### [NEW] Controllers/V1/NotificationsController.cs
- `GET /v1/notifications` — Liste avec filtre isRead, type, channel
- `POST /v1/notifications/{notificationId}/read`
- `POST /v1/notifications/read-all`
- `GET /v1/notifications/preferences`
- `PUT /v1/notifications/preferences`
- `DELETE /v1/notifications/{notificationId}`

#### [NEW] Controllers/V1/AdminController.cs
- `GET /v1/admin/tenants` — Liste tenants (Platform Admin only)
- `GET /v1/admin/tenants/{tenantId}`
- `POST /v1/admin/tenants/{tenantId}/suspend`
- `POST /v1/admin/tenants/{tenantId}/activate`
- `GET /v1/admin/feature-flags`
- `PUT /v1/admin/feature-flags/{key}`
- `GET /v1/admin/audit-logs` — Paginé + filtré (action, userId, dateFrom, dateTo)
- `GET /v1/admin/audit-logs/{eventId}`

---

### Phase 6 — Program.cs (Wiring complet)

#### [MODIFY] Program.cs
```csharp
var builder = WebApplication.CreateBuilder(args);

// ── Controllers + OpenAPI ──────────────────────────────────────────────
builder.Services.AddControllers(o => o.Filters.Add<RequireTenantFilter>())
    .AddJsonOptions(o => { o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull; });

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ── HTTP Context ───────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();

// ── Multi-Tenancy (Scoped) ─────────────────────────────────────────────
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// ── Infrastructure (EF Core, Repos, Security, Storage, AI, Audit) ─────
builder.Services.AddInfrastructure(builder.Configuration);

// ── Rate Limiting ──────────────────────────────────────────────────────
builder.Services.AddRateLimiter(o => {
    o.AddFixedWindowLimiter("auth", opt => { opt.Window = TimeSpan.FromMinutes(1); opt.PermitLimit = 10; });
    o.AddFixedWindowLimiter("global", opt => { opt.Window = TimeSpan.FromMinutes(1); opt.PermitLimit = 1000; });
});

// ── Health Checks ──────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection")!)
    .AddCheck("api", () => HealthCheckResult.Healthy());

// ── Background Jobs (Hangfire) ─────────────────────────────────────────
builder.Services.AddHangfire(c => c.UsePostgreSqlStorage(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHangfireServer();

// ── OpenTelemetry ──────────────────────────────────────────────────────
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddEntityFrameworkCoreInstrumentation());

// ── CORS ───────────────────────────────────────────────────────────────
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// ── Middleware Pipeline (ordre critique) ───────────────────────────────
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseRateLimiter();
app.UseHttpsRedirection();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<BearerTokenMiddleware>();
app.UseMiddleware<TenantResolutionMiddleware>();

// ── Health + OpenAPI ───────────────────────────────────────────────────
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" })).AllowAnonymous();
app.MapGet("/health/ready", async (EaiosDbContext db) => {
    var ok = await db.Database.CanConnectAsync();
    return ok ? Results.Ok(new { status = "Healthy" }) : Results.Problem("DB unavailable");
}).AllowAnonymous();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHangfireDashboard("/admin/jobs");
app.MapControllers();

// ── Bootstrap DB (dev) ─────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EaiosDbContext>();
    await db.Database.MigrateAsync();
    await SystemPermissionsSeed.SeedAsync(db);
}

app.Run();
public partial class Program;
```

---

### Phase 7 — Configuration appsettings.json

#### [MODIFY] appsettings.json
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=eaios;Username=eaios;Password=eaios_dev"
  },
  "Security": {
    "TokenSigningKey": "CHANGE_ME_IN_PRODUCTION_MIN_64_CHARS",
    "TokenSigningKeyPublic": "",
    "AccessTokenLifetimeMinutes": 15,
    "RefreshTokenLifetimeDays": 7,
    "Argon2MemoryCost": 65536,
    "Argon2Iterations": 3,
    "Argon2Parallelism": 4
  },
  "Storage": {
    "Provider": "Local",
    "LocalBasePath": "wwwroot/uploads",
    "BaseUrl": "http://localhost:5000/uploads",
    "MaxFileSizeBytes": 10485760,
    "MaxMultipartSizeBytes": 5368709120,
    "AllowedMimeTypes": ["application/pdf", "application/vnd.openxmlformats-officedocument.*", "image/*", "text/*"]
  },
  "Ai": {
    "Provider": "Stub",
    "EmbeddingDimensions": 3072
  },
  "Hangfire": {
    "DashboardEnabled": true
  },
  "RateLimit": {
    "AuthWindowMinutes": 1,
    "AuthPermitLimit": 10,
    "GlobalWindowMinutes": 1,
    "GlobalPermitLimit": 1000
  }
}
```

#### [MODIFY] appsettings.Development.json
```json
{
  "Logging": { "LogLevel": { "Default": "Debug", "Microsoft.EntityFrameworkCore.Database.Command": "Information" } },
  "Storage": { "Provider": "Local" },
  "Ai": { "Provider": "Stub" }
}
```

---

## Verification Plan

### Build
```powershell
dotnet build
```

### EF Core Migrations (après implémentation)
```powershell
dotnet ef migrations add InitialCreate --context EaiosDbContext
dotnet ef database update --context EaiosDbContext
```

### Manual Smoke Tests
```
POST /v1/auth/bootstrap              → 201 Created
POST /v1/auth/login                  → 200 OK avec JWT
GET  /v1/users/me                    → 200 OK profil complet
POST /v1/organization/invitations    → 201 Created
POST /v1/roles                       → 201 Created
POST /v1/resources/upload            → 201 + indexingStatus: Pending
POST /v1/search                      → 200 avec résultats simulés
POST /v1/agents/{id}/execute         → 200 avec réponse simulée
GET  /health/live                    → 200 Healthy
GET  /health/ready                   → 200 ou 503 selon DB
```

### OpenAPI
- Accessible sur `/openapi/v1.json` en dev
- Tous les endpoints documentés avec `[ProducesResponseType]`

---

## Notes de Design (Décisions Architecturales)

1. **EF Core conditionnel** — En dev sans PostgreSQL, l'InMemoryDatabase reste utilisable via flag `UseInMemoryDatabase`. La migration vers PostgreSQL est transparente.
2. **Stub Services** — MinIO, Qdrant, Elasticsearch, LLM sont stubbed en dev avec réponses plausibles. Le contrat d'interface permet le swap sans changer les controllers.
3. **Global Query Filters** — Un développeur ne peut PAS accidentellement faire une requête cross-tenant. La contrainte est architecturale, pas conventionnelle.
4. **Audit immuable** — `AuditEvent` dans un `PlatformDbContext` séparé avec règles PostgreSQL `NO DELETE / NO UPDATE`.
5. **SSE Streaming** — Utilisé pour `/agents/{id}/execute/stream` via `IAsyncEnumerable<string>`.
6. **RFC 7807** — Toutes les erreurs passent par `GlobalExceptionHandler` → `ProblemDetails` uniformes.
7. **RGPD** — Suppression logique + planification anonymisation via Hangfire background job.
