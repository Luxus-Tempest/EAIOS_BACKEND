using EAIOS.Api.Domain.Organization;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Organization;

namespace EAIOS.Api.Application.Organization;

public interface IWorkspaceService
{
    Task<Workspace> CreateWorkspaceAsync(Guid tenantId, string name, Guid ownerId, WorkspaceType type, string? description, string? color, string? iconCode, CancellationToken ct = default);
    Task<Workspace> UpdateWorkspaceAsync(Guid id, string name, string? description, string? color, string? iconCode, CancellationToken ct = default);
    Task DeleteWorkspaceAsync(Guid id, CancellationToken ct = default);
    Task<Membership> AddMemberAsync(Guid tenantId, Guid workspaceId, Guid userId, CancellationToken ct = default);
    Task RemoveMemberAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);
}

public sealed class WorkspaceService(
    IWorkspaceRepository workspaceRepo,
    IMembershipRepository membershipRepo) : IWorkspaceService
{
    public async Task<Workspace> CreateWorkspaceAsync(Guid tenantId, string name, Guid ownerId, WorkspaceType type, string? description, string? color, string? iconCode, CancellationToken ct = default)
    {
        var existing = await workspaceRepo.GetPagedAsync(1, 1, w => w.Name == name, null, ct);
        if (existing.TotalCount > 0)
            throw new InvalidOperationException("NAME_ALREADY_EXISTS");

        var ws = Workspace.Create(tenantId, name, ownerId, type, description);
        if (color != null || iconCode != null) ws.Update(null, null, color, iconCode);

        await workspaceRepo.AddAsync(ws, ct);
        await workspaceRepo.SaveAsync(ct);
        return ws;
    }

    public async Task<Workspace> UpdateWorkspaceAsync(Guid id, string name, string? description, string? color, string? iconCode, CancellationToken ct = default)
    {
        var ws = await workspaceRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();

        if (!string.IsNullOrWhiteSpace(name) && !name.Equals(ws.Name, StringComparison.OrdinalIgnoreCase))
        {
            var existing = await workspaceRepo.GetPagedAsync(1, 1, w => w.Name == name, null, ct);
            if (existing.TotalCount > 0) throw new InvalidOperationException("NAME_ALREADY_EXISTS");
        }

        ws.Update(name, description, color, iconCode);
        workspaceRepo.Update(ws);
        await workspaceRepo.SaveAsync(ct);
        return ws;
    }

    public async Task DeleteWorkspaceAsync(Guid id, CancellationToken ct = default)
    {
        var ws = await workspaceRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        workspaceRepo.SoftDelete(ws);
        await workspaceRepo.SaveAsync(ct);
    }

    public async Task<Membership> AddMemberAsync(Guid tenantId, Guid workspaceId, Guid userId, CancellationToken ct = default)
    {
        var existing = await membershipRepo.FindAsync(userId, workspaceId, null, ct);
        if (existing != null) throw new InvalidOperationException("ALREADY_MEMBER");

        var membership = Membership.Create(tenantId, userId, MembershipType.Member, workspaceId: workspaceId);
        await membershipRepo.AddAsync(membership, ct);
        await membershipRepo.SaveAsync(ct);
        return membership;
    }

    public async Task RemoveMemberAsync(Guid workspaceId, Guid userId, CancellationToken ct = default)
    {
        var membership = await membershipRepo.FindAsync(userId, workspaceId, null, ct) ?? throw new KeyNotFoundException();
        membershipRepo.SoftDelete(membership);
        await membershipRepo.SaveAsync(ct);
    }
}
