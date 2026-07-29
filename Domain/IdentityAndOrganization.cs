namespace EAIOS.Api.Domain;

public sealed class Organization
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public string Plan { get; set; } = "trial";
    public string DefaultLanguage { get; set; } = "fr";
    public string TimeZone { get; set; } = "Africa/Casablanca";
}

public sealed class User
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid OrganizationId { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string PasswordHash { get; set; }
    public bool IsActive { get; set; } = true;
    public HashSet<string> Roles { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class Session
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid UserId { get; init; }
    public required string RefreshTokenHash { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
}

public sealed class Workspace
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid OrganizationId { get; init; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string Type { get; set; } = "Project";
    public bool IsDeleted { get; set; }
    public HashSet<Guid> MemberIds { get; } = [];
}

public sealed class Department
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid OrganizationId { get; init; }
    public required string Name { get; set; }
    public required string Code { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? ManagerId { get; set; }
    public bool IsDeleted { get; set; }
    public HashSet<Guid> MemberIds { get; } = [];
}
