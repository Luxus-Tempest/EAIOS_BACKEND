using EAIOS.Api.Domain.Connector;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;

namespace EAIOS.Api.Application.Connector;

public sealed class ConnectorCatalogService(
    IConnectorDefinitionRepository definitionRepo) : IConnectorCatalogService
{
    public async Task<IReadOnlyList<ConnectorDefinition>> GetCatalogAsync(CancellationToken ct = default)
    {
        return await definitionRepo.GetAllAsync(ct);
    }

    public async Task<ConnectorDefinition> GetDefinitionAsync(Guid id, CancellationToken ct = default)
    {
        return await definitionRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Définition de connecteur introuvable.");
    }
}
