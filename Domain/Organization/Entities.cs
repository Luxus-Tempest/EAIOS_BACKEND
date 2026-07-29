using EAIOS.Api.Domain.Shared.Primitives;

namespace EAIOS.Api.Domain.Organization;

// ═══════════════════════════════════════════════════════════════════════════════
// ENUMS
// ═══════════════════════════════════════════════════════════════════════════════

public enum OrganizationStatus { Trial, Active, Suspended, Expired, Cancelled }
public enum WorkspaceType { Standard, Private, Public, External }
public enum WorkspaceStatus { Active, Archived, Deleted }
public enum DepartmentStatus { Active, Inactive }
public enum MembershipType { Owner, Admin, Member, Guest }
public enum MembershipStatus { Active, Inactive, Suspended }

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: Organization
// Table: platform.organizations (non-tenant scoped — global)
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class Organization : Entity<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public string? Domain { get; set; }
    public string? Website { get; set; }
    public OrganizationStatus Status { get; set; }
    public string DefaultLanguage { get; set; } = "fr";
    public string TimeZone { get; set; } = "Europe/Paris";
    public string? Address { get; set; }
    public string? Country { get; set; }
    public string? Industry { get; set; }
    public int EmployeeCount { get; set; }

    // ── Storage & Quotas ───────────────────────────────────────────────────────
    public long StorageQuotaBytes { get; set; } = 10L * 1024 * 1024 * 1024; // 10 GB default
    public long StorageUsedBytes { get; set; }
    public int MaxUsers { get; set; } = 50;
    public int CurrentUsers { get; set; }
    public int MonthlyTokenQuota { get; set; } = 10_000_000;
    public int MonthlyTokensUsed { get; set; }

    // ── Licence ────────────────────────────────────────────────────────────────
    public string PlanId { get; set; } = "free";
    public DateTime? TrialEndsAt { get; set; }
    public DateTime? SubscriptionEndsAt { get; set; }

    // ── PostgreSQL ─────────────────────────────────────────────────────────────
    public string? SchemaName { get; set; }  // org_{id_no_dashes}

    // ── Security ───────────────────────────────────────────────────────────────
    public bool MfaRequired { get; set; }
    public bool SsoEnabled { get; set; }
    public string? AllowedIpRanges { get; set; } // JSON array
    public string? SsoConfig { get; set; }        // JSONB

    // ── Branding ───────────────────────────────────────────────────────────────
    public string? PrimaryColor { get; set; }
    public string? CustomEmailDomain { get; set; }

    public static Organization Create(string name, string slug)
    {
        var id = Guid.CreateVersion7();
        return new Organization
        {
            Id = id,
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            Status = OrganizationStatus.Trial,
            SchemaName = $"org_{id.ToString("N")}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: Workspace
// Table: org_{id}.organization.workspaces
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class Workspace : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public WorkspaceType Type { get; private set; }
    public WorkspaceStatus Status { get; private set; }
    public string? Color { get; private set; }
    public string? IconCode { get; private set; }
    public Guid? DepartmentId { get; private set; }
    public Guid OwnerId { get; private set; }
    public bool IsDefault { get; private set; }
    public int MemberCount { get; private set; }
    public long StorageUsedBytes { get; private set; }
    public string[] Tags { get; private set; } = [];

    public static Workspace Create(Guid organizationId, string name, Guid ownerId,
        WorkspaceType type = WorkspaceType.Standard, string? description = null)
    {
        var ws = new Workspace
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            Description = description,
            Type = type,
            Status = WorkspaceStatus.Active,
            OwnerId = ownerId
        };
        ws.SetOrganizationId(organizationId);
        ws.SetCreated(ownerId);
        return ws;
    }

    public void Update(string? name, string? description, string? color, string? iconCode)
    {
        if (!string.IsNullOrWhiteSpace(name)) Name = name.Trim();
        if (description is not null) Description = description;
        if (color is not null) Color = color;
        if (iconCode is not null) IconCode = iconCode;
    }

    public void Archive() => Status = WorkspaceStatus.Archived;
    public void IncrementMembers() => MemberCount++;
    public void DecrementMembers() { if (MemberCount > 0) MemberCount--; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: Department
// Table: org_{id}.organization.departments
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class Department : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? Code { get; private set; }
    public DepartmentStatus Status { get; private set; }
    public Guid? ParentId { get; private set; }
    public Guid? ManagerId { get; private set; }
    public string? Color { get; private set; }
    public string? IconCode { get; private set; }
    public int MemberCount { get; private set; }
    public long StorageQuotaBytes { get; private set; }
    public long StorageUsedBytes { get; private set; }

    public static Department Create(Guid organizationId, string name, Guid createdBy,
        Guid? parentId = null, string? description = null)
    {
        var dept = new Department
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            Description = description,
            Status = DepartmentStatus.Active,
            ParentId = parentId
        };
        dept.SetOrganizationId(organizationId);
        dept.SetCreated(createdBy);
        return dept;
    }

    public void Update(string? name, string? description, Guid? managerId, string? code)
    {
        if (!string.IsNullOrWhiteSpace(name)) Name = name.Trim();
        if (description is not null) Description = description;
        if (managerId.HasValue) ManagerId = managerId;
        if (!string.IsNullOrWhiteSpace(code)) Code = code;
    }

    public void Deactivate() => Status = DepartmentStatus.Inactive;
    public void IncrementMembers() => MemberCount++;
    public void DecrementMembers() { if (MemberCount > 0) MemberCount--; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: Membership
// Table: org_{id}.organization.memberships
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class Membership : TenantEntity
{
    public Guid UserId { get; private set; }
    public Guid? WorkspaceId { get; private set; }
    public Guid? DepartmentId { get; private set; }
    public MembershipType Type { get; private set; }
    public MembershipStatus Status { get; private set; }
    public DateTime? JoinedAt { get; private set; }

    public static Membership Create(Guid organizationId, Guid userId, MembershipType type,
        Guid? workspaceId = null, Guid? departmentId = null)
    {
        var m = new Membership
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            WorkspaceId = workspaceId,
            DepartmentId = departmentId,
            Type = type,
            Status = MembershipStatus.Active,
            JoinedAt = DateTime.UtcNow
        };
        m.SetOrganizationId(organizationId);
        m.SetCreated(userId);
        return m;
    }

    public void Suspend() => Status = MembershipStatus.Suspended;
    public void Activate() => Status = MembershipStatus.Active;
}
