using EAIOS.Api.Domain.Connector;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.Persistence.Seeds;

/// <summary>
/// Catalogue des connecteurs — niveau plateforme, hors tenant.
///
/// <para>
/// Le catalogue n'était amorcé nulle part : l'écran « Connecteurs » listait
/// zéro source raccordable, et personne ne pouvait créer une instance. Ce seed
/// pose les définitions que le produit sait décrire aujourd'hui, avec leur
/// schéma de configuration (<c>required</c> est lu par
/// <c>ConnectorService.FindMissingRequiredFields</c>). Idempotent par
/// <c>Slug</c> ; une définition existante n'est jamais écrasée.
/// </para>
/// <para>
/// La synchronisation réelle d'une source dépend d'un moteur
/// (<c>IConnectorSyncEngine</c>) : sans moteur, la synchronisation le dit.
/// </para>
/// </summary>
public static class ConnectorCatalogSeed
{
    private sealed record Definition(
        string Slug, string Name, string Description, ConnectorCategory Category, ConnectorAuthType AuthType,
        string[] Required, string[] Secrets, string[] Capabilities);

    private static readonly Definition[] Catalog =
    [
        new("sharepoint-online", "SharePoint Online",
            "Bibliothèques de documents d'un site SharePoint (Microsoft 365), par application déléguée.",
            ConnectorCategory.Ecm, ConnectorAuthType.OAuth2,
            Required: ["siteUrl", "tenantId", "clientId", "clientSecret"], Secrets: ["clientSecret"],
            Capabilities: ["import", "export", "versions", "metadata"]),
        new("google-drive", "Google Drive",
            "Dossiers partagés d'un espace Google Workspace.",
            ConnectorCategory.Storage, ConnectorAuthType.OAuth2,
            Required: ["clientId", "clientSecret", "refreshToken"], Secrets: ["clientSecret", "refreshToken"],
            Capabilities: ["import", "versions"]),
        new("smb-share", "Partage de fichiers (SMB)",
            "Un partage réseau Windows ou Samba, parcouru à intervalle régulier.",
            ConnectorCategory.Storage, ConnectorAuthType.BasicAuth,
            Required: ["host", "share", "username", "password"], Secrets: ["password"],
            Capabilities: ["import"]),
        new("http-api", "Point d'entrée HTTP (API générique)",
            "Une API qui expose des documents en JSON ; le test de connexion sonde l'adresse déclarée.",
            ConnectorCategory.Custom, ConnectorAuthType.ApiKey,
            Required: ["baseUrl", "apiKey"], Secrets: ["apiKey"],
            Capabilities: ["import", "metadata"]),
        new("imap-mailbox", "Boîte courriel (IMAP)",
            "Les pièces jointes d'une boîte fonctionnelle, dossier par dossier.",
            ConnectorCategory.Communication, ConnectorAuthType.BasicAuth,
            Required: ["host", "username", "password"], Secrets: ["password"],
            Capabilities: ["import"]),
    ];

    public static async Task SeedAsync(PlatformDbContext db, CancellationToken ct = default)
    {
        var existing = await db.ConnectorDefinitions.AsNoTracking().Select(d => d.Slug).ToListAsync(ct);
        var known = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAdd = Catalog
            .Where(d => !known.Contains(d.Slug))
            .Select(d => new ConnectorDefinition
            {
                Id = Guid.CreateVersion7(),
                Slug = d.Slug,
                Name = d.Name,
                Description = d.Description,
                Category = d.Category,
                AuthType = d.AuthType,
                SchemaJson = BuildSchema(d),
                SupportedCapabilities = d.Capabilities,
                Version = "1.0.0",
                IsActive = true,
            })
            .ToList();

        if (toAdd.Count == 0) return;

        await db.ConnectorDefinitions.AddRangeAsync(toAdd, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Un schéma JSON minimal : les clés obligatoires et, pour chacune, si elle est secrète.</summary>
    private static string BuildSchema(Definition d)
    {
        var properties = d.Required.ToDictionary(
            key => key,
            key => new { type = "string", secret = d.Secrets.Contains(key) });
        return System.Text.Json.JsonSerializer.Serialize(new { required = d.Required, properties });
    }
}
