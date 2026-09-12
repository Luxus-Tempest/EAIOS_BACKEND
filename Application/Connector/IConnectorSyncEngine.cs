using EAIOS.Api.Domain.Connector;

namespace EAIOS.Api.Application.Connector;

/// <summary>Ce qu'une passe de synchronisation a réellement produit.</summary>
public sealed record SyncOutcome(int Discovered, int Imported, int Failed, string Message);

/// <summary>
/// Un moteur sait synchroniser une famille de connecteurs (par
/// <see cref="ConnectorDefinition.Slug"/> ou par catégorie). Le service en
/// choisit un par instance ; s'il n'en existe aucun, la synchronisation
/// <b>le dit</b> au lieu de déclarer un succès vide.
///
/// <para>
/// C'est le point d'extension pour une source réelle : un moteur lit la source
/// avec les identifiants déchiffrés, puis dépose les documents par le pipeline
/// d'import habituel, qui les extrait et les indexe comme n'importe quel dépôt.
/// </para>
/// </summary>
public interface IConnectorSyncEngine
{
    bool Supports(ConnectorDefinition definition);

    Task<SyncOutcome> SyncAsync(
        ConnectorInstance instance,
        ConnectorDefinition definition,
        IReadOnlyDictionary<string, string> configuration,
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken ct = default);
}
