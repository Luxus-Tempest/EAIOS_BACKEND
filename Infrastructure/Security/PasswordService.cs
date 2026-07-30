using Isopoh.Cryptography.Argon2;

namespace EAIOS.Api.Infrastructure.Security;

/// <summary>
/// Service de hachage de mots de passe utilisant Argon2id (OWASP recommandé).
/// Paramètres : memory=65536 KiB, iterations=3, parallelism=4.
/// </summary>
public interface IPasswordService
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string hash);
    bool IsStrongPassword(string password);
}

public sealed class PasswordService : IPasswordService
{
    private static readonly Argon2Config _config = new()
    {
        Type                = Argon2Type.HybridAddressing, // Argon2id
        Version             = Argon2Version.Nineteen,
        TimeCost            = 3,
        MemoryCost          = 65536,  // 64 MiB
        Lanes               = 4,
        Threads             = Environment.ProcessorCount,
        HashLength          = 32,
        ClearPassword       = true,
        ClearSecret         = true,
    };

    public string HashPassword(string password)
    {
        return Argon2.Hash(new Argon2Config
        {
            Type        = _config.Type,
            Version     = _config.Version,
            TimeCost    = _config.TimeCost,
            MemoryCost  = _config.MemoryCost,
            Lanes       = _config.Lanes,
            Threads     = _config.Threads,
            HashLength  = _config.HashLength,
            Password    = System.Text.Encoding.UTF8.GetBytes(password),
            ClearPassword = true
        });
    }

    public bool VerifyPassword(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return false;
        try
        {
            return Argon2.Verify(hash, password);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Vérifie les règles de complexité OWASP :
    /// ≥12 caractères, majuscule, minuscule, chiffre, caractère spécial.
    /// </summary>
    public bool IsStrongPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12) return false;
        return password.Any(char.IsUpper)
            && password.Any(char.IsLower)
            && password.Any(char.IsDigit)
            && password.Any(c => !char.IsLetterOrDigit(c));
    }
}
