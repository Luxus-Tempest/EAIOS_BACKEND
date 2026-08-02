using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.Platform;

namespace EAIOS.Api.Application.Platform;

public interface IPlatformAdminService
{
    // Tenants
    Task<PagedResult<TenantSummaryDto>> ListTenantsAsync(int page, int pageSize, string? query, CancellationToken ct = default);
    Task<TenantSummaryDto> GetTenantAsync(Guid id, CancellationToken ct = default);
    Task<TenantSummaryDto> CreateTenantAsync(CreateTenantRequest req, Guid actorId, CancellationToken ct = default);
    Task<TenantSummaryDto> SuspendTenantAsync(Guid id, string reason, Guid actorId, CancellationToken ct = default);
    Task<TenantSummaryDto> ReactivateTenantAsync(Guid id, Guid actorId, CancellationToken ct = default);
    
    // Stats & License
    Task<object> GetTenantStatsAsync(Guid id, CancellationToken ct = default);
    Task<TenantSummaryDto> UpdateTenantLicenseAsync(Guid id, UpdateLicenseRequest req, Guid actorId, CancellationToken ct = default);

    // Feature Flags
    Task<IReadOnlyList<FeatureFlagDto>> ListFeatureFlagsAsync(Guid? organizationId, CancellationToken ct = default);
    Task<FeatureFlagDto> UpdateFeatureFlagAsync(Guid id, UpdateFeatureFlagRequest req, Guid actorId, CancellationToken ct = default);

    // Audit
    Task<PagedResult<AuditEventDto>> ListAuditLogsAsync(Guid? organizationId, string? action, string? actorId, int page, int pageSize, CancellationToken ct = default);
    Task<string> ExportAuditLogsAsync(Guid actorId, CancellationToken ct = default);

    // Diagnostics
    Task<object> GetHealthStatusAsync(CancellationToken ct = default);
    Task<object> GetMetricsAsync(CancellationToken ct = default);
}
