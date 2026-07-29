using EAIOS.Api.Domain.Resource;

namespace EAIOS.Api.Application.Resource;

// ── Document ──────────────────────────────────────────────────────────────────

public sealed record DocumentDto(
    Guid Id,
    string Title,
    string? Description,
    Domain.Resource.ResourceType ResourceType,
    ResourceClassification Classification,
    ResourceStatus Status,
    IndexingStatus IndexingStatus,
    Guid? FolderId,
    Guid? WorkspaceId,
    Guid? DepartmentId,
    Guid OwnerId,
    string? MimeType,
    long FileSizeBytes,
    string? Extension,
    int VersionCount,
    Guid? CurrentVersionId,
    int? PageCount,
    string? Language,
    string[] Tags,
    bool HasLegalHold,
    int ViewCount,
    int DownloadCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid? CreatedBy);

public sealed record DocumentSummaryDto(
    Guid Id,
    string Title,
    string? MimeType,
    long FileSizeBytes,
    ResourceClassification Classification,
    ResourceStatus Status,
    IndexingStatus IndexingStatus,
    string[] Tags,
    DateTime CreatedAt,
    Guid? CreatedBy);

public sealed record UpdateDocumentRequest(
    string? Title,
    string? Description,
    ResourceClassification? Classification,
    Guid? FolderId,
    string[]? Tags);

// ── Upload ────────────────────────────────────────────────────────────────────

public sealed record UploadDocumentRequest(
    string Title,
    string? Description = null,
    ResourceClassification Classification = ResourceClassification.Internal,
    Guid? FolderId = null,
    Guid? WorkspaceId = null,
    Guid? DepartmentId = null,
    string[]? Tags = null,
    string? ChangeNote = null);

public sealed record UploadDocumentResult(
    Guid DocumentId,
    Guid VersionId,
    string OriginalFileName,
    long FileSizeBytes,
    string MimeType,
    IndexingStatus IndexingStatus,
    string? StatusUrl);

public sealed record InitiateMultipartRequest(
    string FileName,
    long TotalSizeBytes,
    string ContentType,
    string? Title = null,
    ResourceClassification Classification = ResourceClassification.Internal,
    Guid? FolderId = null);

public sealed record MultipartInitiateResult(string UploadId, string StorageKey, int ChunkSizeBytes, int TotalChunks);
public sealed record MultipartCompleteRequest(IReadOnlyList<ChunkInfo> Chunks);
public sealed record ChunkInfo(int ChunkIndex, string Checksum);

// ── Version ───────────────────────────────────────────────────────────────────

public sealed record DocumentVersionDto(
    Guid Id,
    Guid DocumentId,
    int VersionNumber,
    string? Label,
    string? ChangeNote,
    DocumentVersionStatus Status,
    bool IsCurrent,
    string OriginalFileName,
    string MimeType,
    long FileSizeBytes,
    string? Checksum,
    int? PageCount,
    bool VirusScanPassed,
    DateTime CreatedAt,
    Guid? CreatedBy);

public sealed record NewVersionRequest(string? ChangeNote = null, string? Label = null);

// ── Folder ────────────────────────────────────────────────────────────────────

public sealed record FolderDto(
    Guid Id,
    string Name,
    Guid? ParentId,
    string Path,
    int Depth,
    Guid? WorkspaceId,
    Guid? DepartmentId,
    FolderStatus Status,
    bool IsSystemFolder,
    string? Color,
    string? IconCode,
    int DocumentCount,
    long SizeBytes,
    DateTime CreatedAt);

public sealed record CreateFolderRequest(
    string Name,
    Guid? ParentId = null,
    Guid? WorkspaceId = null,
    Guid? DepartmentId = null,
    string? Color = null,
    string? IconCode = null);

public sealed record UpdateFolderRequest(string? Name, string? Color, string? IconCode);
public sealed record MoveFolderRequest(Guid? NewParentId);

// ── Metadata ─────────────────────────────────────────────────────────────────

public sealed record MetadataValueDto(string FieldKey, string FieldType, string? Value);
public sealed record UpdateMetadataRequest(IReadOnlyList<MetadataValueInput> Values);
public sealed record MetadataValueInput(string FieldKey, string? Value, string FieldType = "text");

// ── Share ────────────────────────────────────────────────────────────────────

public sealed record DocumentShareDto(
    Guid Id,
    Guid DocumentId,
    ShareTargetType TargetType,
    Guid? TargetId,
    string? TargetEmail,
    SharePermission Permission,
    Guid SharedBy,
    DateTime? ExpiresAt,
    bool IsPublicLink,
    string? PublicLinkUrl,
    int AccessCount,
    DateTime CreatedAt);

public sealed record CreateShareRequest(
    ShareTargetType TargetType,
    Guid? TargetId = null,
    string? TargetEmail = null,
    SharePermission Permission = SharePermission.View,
    DateTime? ExpiresAt = null,
    bool NotifyOnAccess = false);

public sealed record CreatePublicLinkRequest(
    SharePermission Permission = SharePermission.View,
    DateTime? ExpiresAt = null);

public sealed record PublicLinkResult(Guid ShareId, string Token, string Url, DateTime? ExpiresAt);

// ── Legal Hold ───────────────────────────────────────────────────────────────

public sealed record LegalHoldDto(
    Guid Id,
    Guid DocumentId,
    string Reason,
    string? CaseReference,
    LegalHoldStatus Status,
    Guid PlacedBy,
    DateTime PlacedAt,
    DateTime? ReleasedAt,
    Guid? ReleasedBy,
    string? ReleaseReason);

public sealed record CreateLegalHoldRequest(string Reason, string? CaseReference = null);
public sealed record ReleaseLegalHoldRequest(string Reason);
