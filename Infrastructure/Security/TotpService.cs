using OtpNet;
using System.Security.Cryptography;

namespace EAIOS.Api.Infrastructure.Security;

/// <summary>
/// Service TOTP (RFC 6238) pour l'authentification multi-facteur.
/// Utilise OtpNet avec fenêtre de ±1 pas (30 secondes) pour tolérer le décalage horloge.
/// </summary>
public interface ITotpService
{
    string  GenerateSecret();
    string  BuildQrCodeUri(string email, string secret, string issuer = "EAIOS");
    bool    VerifyCode(string secret, string code);
    string[] GenerateBackupCodes(int count = 8);
    string HashBackupCode(string code);
    bool VerifyBackupCode(string code, string hash);
}

public sealed class TotpService : ITotpService
{
    public string GenerateSecret()
    {
        var secretKey = KeyGeneration.GenerateRandomKey(20); // 160 bits
        return Base32Encoding.ToString(secretKey);
    }

    public string BuildQrCodeUri(string email, string secret, string issuer = "EAIOS")
    {
        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedEmail  = Uri.EscapeDataString(email);
        return $"otpauth://totp/{encodedIssuer}:{encodedEmail}?secret={secret}&issuer={encodedIssuer}&algorithm=SHA1&digits=6&period=30";
    }

    public bool VerifyCode(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code)) return false;
        try
        {
            var secretBytes = Base32Encoding.ToBytes(secret);
            var totp = new Totp(secretBytes, step: 30, totpSize: 6);
            return totp.VerifyTotp(
                code.Trim(),
                out _,
                new VerificationWindow(previous: 1, future: 1));
        }
        catch
        {
            return false;
        }
    }

    public string[] GenerateBackupCodes(int count = 8)
    {
        var codes = new string[count];
        for (var i = 0; i < count; i++)
        {
            var bytes = new byte[5];
            RandomNumberGenerator.Fill(bytes);
            var value = BitConverter.ToUInt64([.. bytes, 0, 0, 0], 0) % 100_000_000;
            codes[i] = value.ToString("D8");
        }
        return codes;
    }

    public string HashBackupCode(string code)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(code.Trim());
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public bool VerifyBackupCode(string code, string hash)
    {
        var expected = HashBackupCode(code);
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(expected),
            System.Text.Encoding.UTF8.GetBytes(hash));
    }
}

/// <summary>
/// Service de génération et de hachage des clés API.
/// Format : eak_{64 hex chars}
/// Stockage : seul le SHA-256 de la clé est persisté.
/// </summary>
public interface IApiKeyService
{
    (string FullKey, string KeyPrefix, string KeyHash) Generate(string prefix = "eak");
    string Hash(string fullKey);
}

public sealed class ApiKeyService : IApiKeyService
{
    public (string FullKey, string KeyPrefix, string KeyHash) Generate(string prefix = "eak")
    {
        var rawBytes = new byte[32];
        RandomNumberGenerator.Fill(rawBytes);
        var keyBody  = Convert.ToHexStringLower(rawBytes);
        var fullKey  = $"{prefix}_{keyBody}";
        var keyPrefix = fullKey[..Math.Min(12, fullKey.Length)];
        var keyHash  = Hash(fullKey);
        return (fullKey, keyPrefix, keyHash);
    }

    public string Hash(string fullKey)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(fullKey);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
