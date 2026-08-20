using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;

namespace EAIOS.Api.Infrastructure.Security;

/// <summary>
/// Chiffre les secrets de connecteurs (clés API, mots de passe, jetons OAuth)
/// avant leur écriture en base. Sans cela, <c>ConnectorInstance.CredentialsEncrypted</c>
/// ne contiendrait que du texte clair malgré son nom.
///
/// S'appuie sur ASP.NET Core Data Protection : les clés sont gérées et tournées
/// par le framework, et le chiffrement est lié à une purpose string dédiée.
/// </summary>
public interface ICredentialProtector
{
    string Protect(IReadOnlyDictionary<string, string> credentials);
    IReadOnlyDictionary<string, string> Unprotect(string? protectedPayload);

    string ProtectValue(string plainText);
    string? UnprotectValue(string? protectedValue);
}

public sealed class CredentialProtector(
    IDataProtectionProvider provider,
    ILogger<CredentialProtector> logger) : ICredentialProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("EAIOS.Connector.Credentials.v1");

    public string Protect(IReadOnlyDictionary<string, string> credentials) =>
        _protector.Protect(JsonSerializer.Serialize(credentials));

    public IReadOnlyDictionary<string, string> Unprotect(string? protectedPayload)
    {
        if (string.IsNullOrWhiteSpace(protectedPayload))
            return new Dictionary<string, string>();

        try
        {
            var json = _protector.Unprotect(protectedPayload);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (Exception ex)
        {
            // Charge utile écrite avec un anneau de clés différent, ou corrompue.
            // On renvoie un jeu vide : l'appelant traitera cela comme des
            // identifiants manquants plutôt que de faire tomber la requête.
            logger.LogWarning(ex, "Déchiffrement des identifiants de connecteur impossible.");
            return new Dictionary<string, string>();
        }
    }

    public string ProtectValue(string plainText) => _protector.Protect(plainText);

    public string? UnprotectValue(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue)) return null;

        try   { return _protector.Unprotect(protectedValue); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Déchiffrement d'une valeur protégée impossible.");
            return null;
        }
    }
}
