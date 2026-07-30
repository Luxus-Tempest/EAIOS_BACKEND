using EAIOS.Api.Domain.Resource;

namespace EAIOS.Api.Application.Resource;

public sealed record CreateFolderRequest(
    string  Name,
    Guid?   ParentId      = null,
    Guid?   WorkspaceId   = null,
    Guid?   DepartmentId  = null,
    string? Description   = null);

public sealed record UpdateDocumentRequest(
    string?                  Title,
    string?                  Description,
    ResourceClassification?  Classification);

public sealed record CreateShareRequest(
    DocumentShareType     Type,
    DateTimeOffset?       ExpiresAt       = null,
    SharePermissionLevel  PermissionLevel = SharePermissionLevel.View,
    string?               Password        = null,
    string[]?             RecipientEmails = null);

public sealed record CreateLegalHoldRequest(
    string  CaseName,
    string? CaseReference,
    string  Reason);

public sealed record ReleaseLegalHoldRequest(string ReleaseReason);
