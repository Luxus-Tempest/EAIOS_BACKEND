namespace EAIOS.Api.Application.Common.Interfaces;

/// <summary>Provides current authenticated user context to application/domain services.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? OrganizationId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
    bool IsPlatformAdmin { get; }
    bool IsOrganizationAdmin { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<string> Permissions { get; }
    bool HasRole(string role);
    bool HasPermission(string permission);
}

/// <summary>Current tenant context — isolates all DB queries by OrganizationId.</summary>
public interface ITenantContext
{
    Guid OrganizationId { get; }
    string TenantSchemaName { get; }
    bool IsResolved { get; }
    void SetTenant(Guid organizationId);
}

/// <summary>Audit service — append-only event recording.</summary>
public interface IAuditService
{
    Task LogAsync(string action, AuditLogOptions? options = null, CancellationToken ct = default);
}

public sealed class AuditLogOptions
{
    public string? Module { get; set; }
    public Guid? ResourceId { get; set; }
    public string? ResourceType { get; set; }
    public string? ResourceName { get; set; }
    public object? OldValues { get; set; }
    public object? NewValues { get; set; }
    public bool IsFailure { get; set; }
    public string? FailureReason { get; set; }
}

/// <summary>Notification service — deliver in-app and channel notifications.</summary>
public interface INotificationService
{
    Task SendAsync(Guid organizationId, Guid recipientId, string type, string title,
        string? body = null, string? actionUrl = null, string? actionLabel = null,
        string? dataJson = null, CancellationToken ct = default);

    Task SendBulkAsync(Guid organizationId, IEnumerable<Guid> recipientIds, string type,
        string title, string? body = null, CancellationToken ct = default);
}

/// <summary>File storage abstraction (local dev / MinIO / Azure Blob).</summary>
public interface IStorageService
{
    Task<StorageUploadResult> UploadAsync(Stream content, string fileName, string contentType,
        string organizationId, CancellationToken ct = default);

    Task<string> GetDownloadUrlAsync(string storageKey, TimeSpan? expiry = null, CancellationToken ct = default);
    Task<string?> GetPreviewUrlAsync(string storageKey, CancellationToken ct = default);
    Task DeleteAsync(string storageKey, CancellationToken ct = default);
    Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default);

    Task<MultipartSession> InitiateMultipartAsync(string fileName, long totalSizeBytes,
        string contentType, string organizationId, CancellationToken ct = default);

    Task UploadChunkAsync(string uploadId, int chunkIndex, Stream data, CancellationToken ct = default);

    Task<StorageUploadResult> CompleteMultipartAsync(string uploadId, CancellationToken ct = default);

    Task AbortMultipartAsync(string uploadId, CancellationToken ct = default);
}

public sealed record StorageUploadResult(
    string StorageKey,
    string OriginalFileName,
    long FileSizeBytes,
    string MimeType,
    string? Checksum);

public sealed record MultipartSession(
    string UploadId,
    string StorageKey,
    DateTime ExpiresAt);

/// <summary>Permission evaluation — RBAC → ABAC → Resource Policy (3 layers).</summary>
public interface IPermissionService
{
    Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken ct = default);
    Task<bool> HasResourcePermissionAsync(Guid userId, string permission, Guid resourceId, string resourceType, CancellationToken ct = default);
    Task<string[]> GetUserPermissionsAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>LLM / AI service abstraction (OpenAI, Mistral, stub).</summary>
public interface ILlmService
{
    Task<LlmGenerateResult> GenerateAsync(string systemPrompt, string userInput,
        string model = "gpt-4o", float temperature = 0.7f, CancellationToken ct = default);

    IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userInput,
        string model = "gpt-4o", float temperature = 0.7f, CancellationToken ct = default);

    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);
}

public sealed record LlmGenerateResult(
    string Output,
    int PromptTokens,
    int CompletionTokens,
    string ModelUsed,
    decimal CostUsd);
