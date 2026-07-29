using EAIOS.Api.Domain.Shared.Primitives;

namespace EAIOS.Api.Domain.Resource;

// ═══════════════════════════════════════════════════════════════════════════════
// ENUMS
// ═══════════════════════════════════════════════════════════════════════════════

public enum ResourceType { Document, Link, Data, Form }
public enum ResourceClassification { Public, Internal, Confidential, StrictlyConfidential }
public enum ResourceStatus { Active, Trashed, Archived, Deleted }
public enum IndexingStatus { NotIndexed, PendingScan, PendingParsing, PendingIndexing, Indexed, Failed, Quarantined }
public enum DocumentVersionStatus { Uploaded, PendingScan, PendingParsing, PendingIndexing, Active, Archived, Deleted, Quarantined }
public enum ShareTargetType { User, Department, Workspace, ExternalEmail }
public enum SharePermission { View, Download, Edit, Manage }
public enum LegalHoldStatus { Active, Released }
public enum FolderStatus { Active, Archived }

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: Document (Resource)
// Table: org_{id}.resource.documents
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class Document : TenantEntity
{
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public ResourceType ResourceType { get; private set; }
    public ResourceClassification Classification { get; private set; }
    public ResourceStatus Status { get; private set; }

    // ── Location ───────────────────────────────────────────────────────────────
    public Guid? FolderId { get; private set; }
    public Guid? WorkspaceId { get; private set; }
    public Guid? DepartmentId { get; private set; }

    // ── Ownership ──────────────────────────────────────────────────────────────
    public Guid OwnerId { get; private set; }

    // ── File ───────────────────────────────────────────────────────────────────
    public string? MimeType { get; private set; }
    public long FileSizeBytes { get; private set; }
    public string? Extension { get; private set; }

    // ── Versioning ─────────────────────────────────────────────────────────────
    public int VersionCount { get; private set; }
    public Guid? CurrentVersionId { get; private set; }

    // ── Indexing ───────────────────────────────────────────────────────────────
    public IndexingStatus IndexingStatus { get; private set; }
    public string? ExtractedText { get; private set; }
    public int? PageCount { get; private set; }
    public string? Language { get; private set; }

    // ── Metadata ───────────────────────────────────────────────────────────────
    public string[] Tags { get; private set; } = [];
    public string? ExternalUrl { get; private set; }          // For link-type resources
    public string? ExternalSourceId { get; private set; }     // Connector reference

    // ── Retention ──────────────────────────────────────────────────────────────
    public DateTime? RetentionExpiresAt { get; private set; }
    public bool HasLegalHold { get; private set; }

    // ── Stats ──────────────────────────────────────────────────────────────────
    public int ViewCount { get; private set; }
    public int DownloadCount { get; private set; }

    // ── Relations ──────────────────────────────────────────────────────────────
    public IReadOnlyList<DocumentVersion> Versions { get; private set; } = [];
    public IReadOnlyList<DocumentShare> Shares { get; private set; } = [];
    public IReadOnlyList<MetadataValue> MetadataValues { get; private set; } = [];

    public static Document Create(Guid organizationId, string title, Guid ownerId,
        ResourceType type = ResourceType.Document,
        ResourceClassification classification = ResourceClassification.Internal,
        Guid? folderId = null, Guid? workspaceId = null, Guid? departmentId = null)
    {
        var doc = new Document
        {
            Id = Guid.CreateVersion7(),
            Title = title.Trim(),
            ResourceType = type,
            Classification = classification,
            Status = ResourceStatus.Active,
            OwnerId = ownerId,
            FolderId = folderId,
            WorkspaceId = workspaceId,
            DepartmentId = departmentId,
            IndexingStatus = IndexingStatus.NotIndexed
        };
        doc.SetOrganizationId(organizationId);
        doc.SetCreated(ownerId);
        return doc;
    }

    public void SetCurrentVersion(Guid versionId, string mimeType, long sizeBytes, string? extension, int? pageCount)
    {
        CurrentVersionId = versionId;
        MimeType = mimeType;
        FileSizeBytes = sizeBytes;
        Extension = extension;
        PageCount = pageCount;
        VersionCount++;
        IndexingStatus = IndexingStatus.PendingScan;
    }

    public void SetIndexed(string? extractedText, int? pageCount, string? language)
    {
        IndexingStatus = IndexingStatus.Indexed;
        ExtractedText = extractedText;
        if (pageCount.HasValue) PageCount = pageCount;
        if (language is not null) Language = language;
    }

    public void SetIndexingFailed() => IndexingStatus = IndexingStatus.Failed;
    public void SetQuarantined() => IndexingStatus = IndexingStatus.Quarantined;
    public void Update(string? title, string? description, ResourceClassification? classification, string[]? tags)
    {
        if (!string.IsNullOrWhiteSpace(title)) Title = title.Trim();
        if (description is not null) Description = description;
        if (classification.HasValue) Classification = classification.Value;
        if (tags is not null) Tags = tags;
    }
    public void MoveToTrash() => Status = ResourceStatus.Trashed;
    public void Restore() => Status = ResourceStatus.Active;
    public void SetLegalHold(bool active) => HasLegalHold = active;
    public void IncrementView() => ViewCount++;
    public void IncrementDownload() => DownloadCount++;
    public void SetTags(string[] tags) => Tags = tags;
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: DocumentVersion
// Table: org_{id}.resource.document_versions
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class DocumentVersion : TenantEntity
{
    public Guid DocumentId { get; private set; }
    public int VersionNumber { get; private set; }
    public string? Label { get; private set; }
    public string? ChangeNote { get; private set; }
    public DocumentVersionStatus Status { get; private set; }
    public bool IsCurrent { get; private set; }

    // ── Storage ────────────────────────────────────────────────────────────────
    public string StorageKey { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string MimeType { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }
    public string? Checksum { get; private set; }         // SHA-256
    public string? PreviewStorageKey { get; private set; }
    public string? ThumbnailStorageKey { get; private set; }

    // ── Parsing Result ─────────────────────────────────────────────────────────
    public string? ExtractedText { get; private set; }
    public int? PageCount { get; private set; }
    public string? Language { get; private set; }
    public bool OcrApplied { get; private set; }

    // ── Security ───────────────────────────────────────────────────────────────
    public bool VirusScanPassed { get; private set; }
    public string? VirusScanResult { get; private set; }
    public DateTime? VirusScannedAt { get; private set; }

    // ── Indexing ───────────────────────────────────────────────────────────────
    public string? ElasticsearchDocId { get; private set; }

    public static DocumentVersion Create(Guid organizationId, Guid documentId, int versionNumber,
        string storageKey, string originalFileName, string mimeType, long fileSizeBytes,
        Guid uploadedBy, string? changeNote = null)
    {
        var v = new DocumentVersion
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            VersionNumber = versionNumber,
            StorageKey = storageKey,
            OriginalFileName = originalFileName,
            MimeType = mimeType,
            FileSizeBytes = fileSizeBytes,
            ChangeNote = changeNote,
            Status = DocumentVersionStatus.Uploaded,
            IsCurrent = true
        };
        v.SetOrganizationId(organizationId);
        v.SetCreated(uploadedBy);
        return v;
    }

    public void MarkScanned(bool passed, string? result) { VirusScanPassed = passed; VirusScanResult = result; VirusScannedAt = DateTime.UtcNow; Status = passed ? DocumentVersionStatus.PendingParsing : DocumentVersionStatus.Quarantined; }
    public void MarkParsed(string? text, int? pages, string? lang, bool ocrApplied) { ExtractedText = text; PageCount = pages; Language = lang; OcrApplied = ocrApplied; Status = DocumentVersionStatus.PendingIndexing; }
    public void MarkIndexed(string? esDocId) { ElasticsearchDocId = esDocId; Status = DocumentVersionStatus.Active; }
    public void SetAsCurrent(bool isCurrent) => IsCurrent = isCurrent;
    public void Archive() => Status = DocumentVersionStatus.Archived;
    public void SetPreviewKeys(string? previewKey, string? thumbnailKey) { PreviewStorageKey = previewKey; ThumbnailStorageKey = thumbnailKey; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: Folder
// Table: org_{id}.resource.folders
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class Folder : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public Guid? ParentId { get; private set; }
    public string Path { get; private set; } = "/";     // Materialized path, e.g. /uuid1/uuid2/
    public int Depth { get; private set; }
    public Guid? WorkspaceId { get; private set; }
    public Guid? DepartmentId { get; private set; }
    public Guid OwnerId { get; private set; }
    public FolderStatus Status { get; private set; }
    public bool IsSystemFolder { get; private set; }
    public string? Color { get; private set; }
    public string? IconCode { get; private set; }
    public int DocumentCount { get; private set; }
    public long SizeBytes { get; private set; }

    public static Folder Create(Guid organizationId, string name, Guid ownerId,
        Guid? parentId = null, string parentPath = "/", int parentDepth = 0,
        Guid? workspaceId = null, Guid? departmentId = null)
    {
        var id = Guid.CreateVersion7();
        var f = new Folder
        {
            Id = id,
            Name = name.Trim(),
            ParentId = parentId,
            Path = $"{parentPath}{id}/",
            Depth = parentDepth + 1,
            OwnerId = ownerId,
            WorkspaceId = workspaceId,
            DepartmentId = departmentId,
            Status = FolderStatus.Active
        };
        f.SetOrganizationId(organizationId);
        f.SetCreated(ownerId);
        return f;
    }

    public void Rename(string newName) => Name = newName.Trim();
    public void Move(Guid? newParentId, string newPath, int newDepth) { ParentId = newParentId; Path = newPath; Depth = newDepth; }
    public void Archive() => Status = FolderStatus.Archived;
    public void IncrementDocumentCount() => DocumentCount++;
    public void DecrementDocumentCount() { if (DocumentCount > 0) DocumentCount--; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: MetadataTemplate
// Table: org_{id}.resource.metadata_templates
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class MetadataTemplate : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; }
    public string FieldsJson { get; private set; } = "[]";   // JSON: MetadataFieldDefinition[]
    public string[] ApplicableResourceTypes { get; private set; } = [];

    public static MetadataTemplate Create(Guid organizationId, string name, Guid createdBy, string fieldsJson = "[]")
    {
        var t = new MetadataTemplate
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            IsActive = true,
            FieldsJson = fieldsJson
        };
        t.SetOrganizationId(organizationId);
        t.SetCreated(createdBy);
        return t;
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: MetadataValue
// Table: org_{id}.resource.metadata_values
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class MetadataValue : TenantEntity
{
    public Guid ResourceId { get; private set; }
    public Guid? TemplateId { get; private set; }
    public string FieldKey { get; private set; } = string.Empty;
    public string FieldType { get; private set; } = "text";  // text, number, date, boolean, list
    public string? Value { get; private set; }               // JSON-serialized value

    public static MetadataValue Create(Guid organizationId, Guid resourceId, string fieldKey, string? value, Guid updatedBy, Guid? templateId = null)
    {
        var mv = new MetadataValue
        {
            Id = Guid.CreateVersion7(),
            ResourceId = resourceId,
            TemplateId = templateId,
            FieldKey = fieldKey,
            Value = value
        };
        mv.SetOrganizationId(organizationId);
        mv.SetCreated(updatedBy);
        return mv;
    }

    public void SetValue(string? value) => Value = value;
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: DocumentShare
// Table: org_{id}.resource.document_shares
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class DocumentShare : TenantEntity
{
    public Guid DocumentId { get; private set; }
    public ShareTargetType TargetType { get; private set; }
    public Guid? TargetId { get; private set; }
    public string? TargetEmail { get; private set; }       // For external shares
    public SharePermission Permission { get; private set; }
    public Guid SharedBy { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public bool NotifyOnAccess { get; private set; }
    public string? PublicLinkToken { get; private set; }   // UUID for public links
    public bool IsPublicLink { get; private set; }
    public int AccessCount { get; private set; }

    public static DocumentShare CreateInternal(Guid organizationId, Guid documentId, ShareTargetType targetType,
        Guid targetId, SharePermission permission, Guid sharedBy, DateTime? expiresAt = null)
    {
        var s = new DocumentShare
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            TargetType = targetType,
            TargetId = targetId,
            Permission = permission,
            SharedBy = sharedBy,
            ExpiresAt = expiresAt,
            IsPublicLink = false
        };
        s.SetOrganizationId(organizationId);
        s.SetCreated(sharedBy);
        return s;
    }

    public static DocumentShare CreatePublicLink(Guid organizationId, Guid documentId, SharePermission permission,
        Guid sharedBy, DateTime? expiresAt = null)
    {
        var token = Guid.NewGuid().ToString("N");
        var s = new DocumentShare
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            TargetType = ShareTargetType.ExternalEmail,
            Permission = permission,
            SharedBy = sharedBy,
            ExpiresAt = expiresAt,
            IsPublicLink = true,
            PublicLinkToken = token
        };
        s.SetOrganizationId(organizationId);
        s.SetCreated(sharedBy);
        return s;
    }

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt < DateTime.UtcNow;
    public void RecordAccess() => AccessCount++;
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: LegalHold
// Table: org_{id}.resource.legal_holds
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class LegalHold : TenantEntity
{
    public Guid DocumentId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? CaseReference { get; private set; }
    public LegalHoldStatus Status { get; private set; }
    public Guid PlacedBy { get; private set; }
    public DateTime PlacedAt { get; private set; }
    public DateTime? ReleasedAt { get; private set; }
    public Guid? ReleasedBy { get; private set; }
    public string? ReleaseReason { get; private set; }

    public static LegalHold Create(Guid organizationId, Guid documentId, string reason,
        Guid placedBy, string? caseReference = null)
    {
        var lh = new LegalHold
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            Reason = reason,
            CaseReference = caseReference,
            Status = LegalHoldStatus.Active,
            PlacedBy = placedBy,
            PlacedAt = DateTime.UtcNow
        };
        lh.SetOrganizationId(organizationId);
        lh.SetCreated(placedBy);
        return lh;
    }

    public void Release(Guid releasedBy, string? reason)
    {
        Status = LegalHoldStatus.Released;
        ReleasedAt = DateTime.UtcNow;
        ReleasedBy = releasedBy;
        ReleaseReason = reason;
    }
}
