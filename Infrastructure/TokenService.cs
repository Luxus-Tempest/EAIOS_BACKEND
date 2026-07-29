using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EAIOS.Api.Domain;

namespace EAIOS.Api.Infrastructure;

public sealed class TokenService(IConfiguration configuration)
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(configuration["Security:TokenSigningKey"] ?? throw new InvalidOperationException("Security:TokenSigningKey is required."));
    private readonly int _accessLifetime = configuration.GetValue("Security:AccessTokenLifetimeMinutes", 15);
    private readonly int _refreshLifetimeDays = configuration.GetValue("Security:RefreshTokenLifetimeDays", 7);

    public TokenPair Issue(User user, Guid sessionId)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_accessLifetime);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new TokenPayload(user.Id, user.OrganizationId, sessionId, user.Roles.ToArray(), expiresAt.ToUnixTimeSeconds()))));
        var signature = Sign(payload);
        return new TokenPair($"{payload}.{signature}", CreateRefreshToken(), expiresAt, DateTimeOffset.UtcNow.AddDays(_refreshLifetimeDays));
    }

    public TokenPayload? Validate(string token)
    {
        var parts = token.Split('.', 2);
        if (parts.Length != 2 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Sign(parts[0])), Encoding.UTF8.GetBytes(parts[1]))) return null;
        try
        {
            var payload = JsonSerializer.Deserialize<TokenPayload>(Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])));
            return payload is null || payload.ExpiresAtUnixSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() ? null : payload;
        }
        catch (FormatException) { return null; }
        catch (JsonException) { return null; }
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string HashPassword(string password, byte[]? salt = null)
    {
        salt ??= RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA512, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }
    public static bool VerifyPassword(string password, string encoded)
    {
        var parts = encoded.Split('.', 2);
        if (parts.Length != 2) return false;
        var actual = Convert.FromBase64String(parts[1]);
        var expected = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[0]), 210_000, HashAlgorithmName.SHA512, actual.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
    private string Sign(string payload) => Convert.ToBase64String(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload)));
    private static string CreateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}

public sealed record TokenPayload(Guid UserId, Guid OrganizationId, Guid SessionId, string[] Roles, long ExpiresAtUnixSeconds);
public sealed record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, DateTimeOffset RefreshTokenExpiresAt);
