using EAIOS.Api.Application.Common.Interfaces;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

namespace EAIOS.Api.Infrastructure.Security;

/// <summary>
/// Service JWT RS256 complet.
/// - Access Token : signé RS256, durée de vie courte (15 min par défaut).
/// - Refresh Token : opaque aléatoire 48 octets, haché SHA-256 en base.
/// </summary>
public interface ITokenService
{
    TokenPairDto Issue(Guid userId, Guid organizationId, Guid sessionId, string[] roles, string[] permissions);
    ClaimsPrincipal? ValidateAccessToken(string token);
    string GenerateRefreshToken();
    string HashRefreshToken(string rawToken);
}

public sealed record TokenPairDto(
    string AccessToken,
    string RefreshToken,
    string RefreshTokenHash,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed class TokenService : ITokenService
{
    private readonly RsaSecurityKey _privateKey;
    private readonly RsaSecurityKey _publicKey;
    private readonly string         _issuer;
    private readonly string         _audience;
    private readonly int            _accessLifetimeMinutes;
    private readonly int            _refreshLifetimeDays;

    public TokenService(IConfiguration config)
    {
        _issuer               = config["Security:Jwt:Issuer"]   ?? "eaios-api";
        _audience             = config["Security:Jwt:Audience"] ?? "eaios-client";
        _accessLifetimeMinutes = config.GetValue("Security:Jwt:AccessTokenLifetimeMinutes", 15);
        _refreshLifetimeDays  = config.GetValue("Security:Jwt:RefreshTokenLifetimeDays", 7);

        // Clé privée RSA depuis la configuration (PEM ou génération dev)
        var privateKeyPem = config["Security:Jwt:RsaPrivateKeyPem"];
        var rsa = RSA.Create();

        if (!string.IsNullOrWhiteSpace(privateKeyPem))
        {
            rsa.ImportFromPem(privateKeyPem.AsSpan());
        }
        else
        {
            // Mode DEV : clé RSA générée en mémoire
            rsa = RSA.Create(2048);
        }

        _privateKey = new RsaSecurityKey(rsa);
        _publicKey  = new RsaSecurityKey(rsa);
    }

    public TokenPairDto Issue(Guid userId, Guid organizationId, Guid sessionId, string[] roles, string[] permissions)
    {
        var now      = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_accessLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("org_id",     organizationId.ToString()),
            new("session_id", sessionId.ToString()),
            new("email",      ""), // will be enriched at controller level if needed
        };

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(permissions.Select(p => new Claim("permission", p)));

        var credentials = new SigningCredentials(_privateKey, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer:   _issuer,
            audience: _audience,
            claims:   claims,
            notBefore: now.UtcDateTime,
            expires:  expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var handler     = new JwtSecurityTokenHandler();
        var accessToken = handler.WriteToken(token);
        var raw         = GenerateRefreshToken();
        var hash        = HashRefreshToken(raw);

        return new TokenPairDto(
            accessToken,
            raw,
            hash,
            expiresAt,
            now.AddDays(_refreshLifetimeDays));
    }

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        var handler    = new JwtSecurityTokenHandler();
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = _issuer,
            ValidateAudience         = true,
            ValidAudience            = _audience,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = _publicKey,
            ClockSkew                = TimeSpan.FromSeconds(30)
        };

        try
        {
            return handler.ValidateToken(token, parameters, out _);
        }
        catch
        {
            return null;
        }
    }

    public string GenerateRefreshToken()
    {
        var bytes = new byte[48];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64Url(bytes);
    }

    public string HashRefreshToken(string rawToken)
    {
        var bytes    = System.Text.Encoding.UTF8.GetBytes(rawToken);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hashBytes);
    }
}
