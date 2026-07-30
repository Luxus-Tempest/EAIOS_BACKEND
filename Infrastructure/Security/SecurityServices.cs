using Isopoh.Cryptography.Argon2;
using OtpNet;
using QRCoder;
using System.Security.Cryptography;

namespace EAIOS.Api.Infrastructure.Security;

public interface IPasswordService
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string hash);
}

public sealed class PasswordService : IPasswordService
{
    public string HashPassword(string password)
    {
        return Argon2.Hash(password);
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
}

public interface ITotpService
{
    string GenerateSecret();
    string GenerateQrCodeUri(string email, string secret, string issuer = "EAIOS");
    bool VerifyCode(string secret, string code);
    string[] GenerateBackupCodes(int count = 8);
}

public sealed class TotpService : ITotpService
{
    public string GenerateSecret()
    {
        var secretKey = KeyGeneration.GenerateRandomKey(20);
        return Base32Encoding.ToString(secretKey);
    }

    public string GenerateQrCodeUri(string email, string secret, string issuer = "EAIOS")
    {
        return $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(email)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}";
    }

    public bool VerifyCode(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code)) return false;
        try
        {
            var secretBytes = Base32Encoding.ToBytes(secret);
            var totp = new Totp(secretBytes);
            return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
        }
        catch
        {
            return false;
        }
    }

    public string[] GenerateBackupCodes(int count = 8)
    {
        var codes = new string[count];
        for (int i = 0; i < count; i++)
        {
            var bytes = new byte[4];
            RandomNumberGenerator.Fill(bytes);
            var code = BitConverter.ToUInt32(bytes, 0) % 100000000;
            codes[i] = code.ToString("D8");
        }
        return codes;
    }
}

public interface IApiKeyService
{
    (string FullKey, string Prefix, string Hash) GenerateApiKey(string prefix = "eak");
    string HashApiKey(string fullKey);
}

public sealed class ApiKeyService : IApiKeyService
{
    public (string FullKey, string Prefix, string Hash) GenerateApiKey(string prefix = "eak")
    {
        var randomBytes = new byte[32];
        RandomNumberGenerator.Fill(randomBytes);
        var keyPart = Convert.ToHexStringLower(randomBytes);
        var fullKey = $"{prefix}_{keyPart}";
        var keyPrefix = fullKey[..12];
        var hash = HashApiKey(fullKey);
        return (fullKey, keyPrefix, hash);
    }

    public string HashApiKey(string fullKey)
    {
        using var sha256 = SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(fullKey);
        var hashBytes = sha256.ComputeHash(bytes);
        return Convert.ToHexStringLower(hashBytes);
    }
}
