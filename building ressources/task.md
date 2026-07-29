# EAIOS Implementation Tasks

## Phase 0 — Project Setup
- [/] Update backend.csproj (packages NuGet)
- [ ] Update appsettings.json + appsettings.Development.json

## Phase 1 — Domain Layer
- [ ] Domain/Shared/Primitives/Entity.cs
- [ ] Domain/Shared/Primitives/TenantEntity.cs
- [ ] Domain/Shared/Primitives/ValueObject.cs
- [ ] Domain/Shared/Interfaces/ (ITenantScoped, ISoftDeletable, IAuditable, IDomainEvent)
- [ ] Domain/Shared/Events/DomainEvents.cs
- [ ] Domain/Identity/Entities.cs
- [ ] Domain/Organization/Entities.cs
- [ ] Domain/AccessControl/Entities.cs
- [ ] Domain/Resource/Entities.cs
- [ ] Domain/Knowledge/Entities.cs
- [ ] Domain/Agent/Entities.cs
- [ ] Domain/Workflow/Entities.cs
- [ ] Domain/Search/Entities.cs
- [ ] Domain/Connector/Entities.cs
- [ ] Domain/Analytics/Entities.cs
- [ ] Domain/Notification/Entities.cs
- [ ] Domain/Platform/Entities.cs

## Phase 2 — Application Layer (DTOs)
- [ ] Application/Common/Models/ (PagedResult, ApiResponse, PermissionCheckResult)
- [ ] Application/Common/Interfaces/ (ICurrentUser, ITenantContext, IAuditService, etc.)
- [ ] Application/Identity/Dtos.cs
- [ ] Application/Organization/Dtos.cs
- [ ] Application/AccessControl/Dtos.cs
- [ ] Application/Resource/Dtos.cs
- [ ] Application/Knowledge/Dtos.cs
- [ ] Application/Agent/Dtos.cs
- [ ] Application/Workflow/Dtos.cs
- [ ] Application/Search/Dtos.cs
- [ ] Application/Connector/Dtos.cs
- [ ] Application/Analytics/Dtos.cs
- [ ] Application/Notification/Dtos.cs
- [ ] Application/Platform/Dtos.cs

## Phase 3 — Infrastructure Layer
- [ ] Infrastructure/Persistence/EaiosDbContext.cs
- [ ] Infrastructure/Persistence/PlatformDbContext.cs
- [ ] Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptor.cs
- [ ] Infrastructure/Persistence/Interceptors/TenantSessionInterceptor.cs
- [ ] Infrastructure/Persistence/Repositories/Base/RepositoryBase.cs
- [ ] Infrastructure/Persistence/Repositories/* (Identity, Org, ACL, Resource, Knowledge, Agent, Workflow, Search, Analytics, Notification)
- [ ] Infrastructure/Persistence/Configurations/* (EF Core configs per entity)
- [ ] Infrastructure/Persistence/Seeds/SystemPermissionsSeed.cs
- [ ] Infrastructure/MultiTenancy/TenantContext.cs
- [ ] Infrastructure/Security/TokenService.cs (étendu JWT RS256)
- [ ] Infrastructure/Security/PasswordService.cs (Argon2id)
- [ ] Infrastructure/Security/TotpService.cs
- [ ] Infrastructure/Security/PermissionService.cs (RBAC+ABAC)
- [ ] Infrastructure/Security/ApiKeyService.cs
- [ ] Infrastructure/Storage/IStorageService.cs + LocalStorageService.cs
- [ ] Infrastructure/AI/ILlmService.cs + StubLlmService.cs
- [ ] Infrastructure/Audit/AuditService.cs
- [ ] Infrastructure/Notifications/InMemoryNotificationService.cs
- [ ] Infrastructure/ServiceExtensions.cs

## Phase 4 — Middleware
- [ ] Middleware/RequestContext.cs (étendu CurrentUser)
- [ ] Middleware/GlobalExceptionHandler.cs (RFC 7807)
- [ ] Middleware/RateLimitingMiddleware.cs

## Phase 5 — Controllers
- [ ] Controllers/V1/V1ApiController.cs (étendu)
- [ ] Controllers/V1/AuthController.cs (complet + MFA)
- [ ] Controllers/V1/UsersController.cs
- [ ] Controllers/V1/OrganizationController.cs (étendu invitations)
- [ ] Controllers/V1/WorkspacesController.cs (compatible)
- [ ] Controllers/V1/DepartmentsController.cs (compatible)
- [ ] Controllers/V1/RolesController.cs
- [ ] Controllers/V1/PermissionsController.cs
- [ ] Controllers/V1/PoliciesController.cs
- [ ] Controllers/V1/ResourcesController.cs
- [ ] Controllers/V1/FoldersController.cs
- [ ] Controllers/V1/KnowledgeController.cs
- [ ] Controllers/V1/AgentsController.cs
- [ ] Controllers/V1/WorkflowsController.cs
- [ ] Controllers/V1/SearchController.cs
- [ ] Controllers/V1/ConnectorsController.cs
- [ ] Controllers/V1/AnalyticsController.cs
- [ ] Controllers/V1/NotificationsController.cs
- [ ] Controllers/V1/AdminController.cs

## Phase 6 — Wiring
- [ ] Program.cs (complet)
- [ ] appsettings.json
