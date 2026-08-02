using EAIOS.Api.Domain.Connector;

namespace EAIOS.Api.Application.Connector;

public interface IConnectorCatalogService
{
    Task<IReadOnlyList<ConnectorDefinition>> GetCatalogAsync(CancellationToken ct = default);
    Task<ConnectorDefinition> GetDefinitionAsync(Guid id, CancellationToken ct = default);
}
